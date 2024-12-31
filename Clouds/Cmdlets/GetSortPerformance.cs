using Mars.Clouds.Cmdlets.Hardware;
using Mars.Clouds.Extensions;
using Mars.Clouds.Las;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management.Automation;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "SortPerformance")]
    public class GetSortPerformance : LasTilesCmdlet
    {
        private readonly CancellationTokenSource cancellationTokenSource;

        [Parameter(HelpMessage = "List of sort sizes to benchmark. Each tiles' points' z and intensity values are concatenated in grid metrics cell order and the combined list is then sequentially chunked by each sort size. Chunks are sorted until no full size chunk remains and the time to sort all of the chunks is added to total time reported for the sort size.")]
        [ValidateNotNullOrEmpty]
        [ValidateRange(0, Int32.MaxValue)]
        public List<int> SortSizes { get; set; }

        [Parameter(HelpMessage = "Size, in point cloud CRS units, of cells in output grid metrics tiles matching the input point cloud tiles. Default is 10 m.")]
        [ValidateRange(0.0F, 1000.0F)] // sanity upper bound
        public double CellSize { get; set; }

        [Parameter(HelpMessage = "Number of times to repeat each sort size over each tile to produce more stable timings. Default is 100.")]
        [ValidateRange(0, 1000000)] // sanity upper bound
        public int Iterations { get; set; }

        [Parameter(HelpMessage = "Number of times to repeat each sort size over each tile before starting timing. Default is 20.")]
        [ValidateRange(0, 10000)] // sanity upper bound
        public int WarmupIterations { get; set; }

        public GetSortPerformance()
        {
            this.cancellationTokenSource = new();

            this.CellSize = Double.NaN;
            this.Iterations = 100;
            this.SortSizes = [ 128, 256, 384, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536, 131072 ];
            this.WarmupIterations = 20;
        }

        protected override void ProcessRecord()
        {
            const string cmdletName = "Get-SortPerformance";
            LasTileGrid lasGrid = this.ReadLasHeadersAndFormGrid(cmdletName);
            if (Double.IsNaN(this.CellSize))
            {
                this.CellSize = 10.0 / lasGrid.Crs.GetLinearUnits();
            }
            double tileSizeInFractionalCellsX = lasGrid.Transform.CellWidth / this.CellSize;
            double tileSizeInFractionalCellsY = Double.Abs(lasGrid.Transform.CellHeight) / this.CellSize;
            int tileSizeInCellsX = (int)tileSizeInFractionalCellsX + 1;
            int tileSizeInCellsY = (int)tileSizeInFractionalCellsY + 1;

            (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) = LasReader.GetPointsToGridMetricsBandwidth();
            HardwareCapabilities hardwareCapabilities = HardwareCapabilities.Current;
            int readThreads = this.GetLasTileReadThreadCount(driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs, minWorkerThreadsPerReadThread: 0);
            int maxUsefulThreads = Int32.Min(lasGrid.NonNullCells, 6 * readThreads);

            int gridReadIndex = -1;
            int sortsCompleted = 0;
            int totalSorts = lasGrid.NonNullCells * this.SortSizes.Count;
            using SemaphoreSlim readSemaphore = new(initialCount: readThreads, maxCount: readThreads);
            SortTimings totalSortTimings = new(this.SortSizes);
            ParallelTasks sortTasks = new(Int32.Min(maxUsefulThreads, this.DataThreads), () =>
            {
                UInt16[]? intensityValues = null;
                byte[]? pointReadBuffer = null;
                SortTimings sortTimings = new(this.SortSizes);
                Stopwatch stopwatch = new();
                GridMetricsPointLists? tilePoints = null;
                float[]? zValues = null;
                for (int tileIndex = Interlocked.Increment(ref gridReadIndex); tileIndex < lasGrid.Cells; tileIndex = Interlocked.Increment(ref gridReadIndex))
                {
                    LasTile? lasTile = lasGrid[tileIndex];
                    if (lasTile == null)
                    {
                        continue; // nothing to do as no tile is present at this grid position
                    }

                    readSemaphore.Wait(this.CancellationTokenSource.Token);
                    if (this.Stopping || this.CancellationTokenSource.IsCancellationRequested)
                    {
                        readSemaphore.Release();
                        return;
                    }

                    // read tile points
                    if (tilePoints == null)
                    {
                        tilePoints = new(lasGrid, lasTile, this.CellSize, tileSizeInCellsX, tileSizeInCellsY, metricsCellMask: null);
                    }
                    else
                    {
                        tilePoints.Reset(lasGrid, lasTile, metricsCellMask: null);
                    }

                    using LasReader pointReader = lasTile.CreatePointReader();
                    pointReader.ReadPointsToGrid(lasTile, tilePoints, ref pointReadBuffer);
                    readSemaphore.Release(); // exit semaphore as .las file has been read
                    if (this.Stopping || this.CancellationTokenSource.IsCancellationRequested)
                    {
                        return;
                    }

                    int acceptedPoints = 0;
                    for (int cellIndex = 0; cellIndex < tilePoints.Cells; ++cellIndex)
                    {
                        acceptedPoints += tilePoints[cellIndex].Count;
                    }
                    if ((intensityValues == null) || (intensityValues.Length < acceptedPoints))
                    {
                        intensityValues = GC.AllocateUninitializedArray<UInt16>(acceptedPoints);
                        zValues = GC.AllocateUninitializedArray<float>(acceptedPoints);
                    }
                    int destinationIndex = 0;
                    for (int cellIndex = 0; cellIndex < tilePoints.Cells; ++cellIndex)
                    {
                        PointListZirnc cellPoints = tilePoints[cellIndex];
                        cellPoints.Intensity.CopyTo(0, intensityValues, destinationIndex, cellPoints.Count);
                        cellPoints.Z.CopyTo(0, zValues!, destinationIndex, cellPoints.Count);
                    }

                    // time sorts
                    // TODO: interchange sort algorithm order to average out potential cache effects
                    for (int sortSizeIndex = 0; sortSizeIndex < this.SortSizes.Count; ++sortSizeIndex)
                    {
                        int sortSize = this.SortSizes[sortSizeIndex];
                        UInt16[] intensitySortBuffer = new UInt16[sortSize];
                        UInt16[] intensityRadixWorkingBuffer = new UInt16[sortSize];
                        float[] zSortBuffer = new float[sortSize];
                        int maxSortIndexInclusive = (acceptedPoints - sortSize) / sortSize;

                        // introspective z sort
                        for (int warmupIteration = 0; warmupIteration < this.WarmupIterations; ++warmupIteration)
                        {
                            for (int sourceIndex = 0; sourceIndex <= maxSortIndexInclusive; sourceIndex += sortSize)
                            {
                                Array.Copy(zValues!, sourceIndex, zSortBuffer, 0, zSortBuffer.Length);
                                Array.Sort(intensitySortBuffer); // in place introspective sort
                            }
                        }
                        stopwatch.Restart();
                        for (int iteration = 0; iteration < this.Iterations; ++iteration)
                        {
                            for (int sourceIndex = 0; sourceIndex <= maxSortIndexInclusive; sourceIndex += sortSize)
                            {
                                //Array.Copy(zValues!, sourceIndex, zSortBuffer, 0, zSortBuffer.Length);
                                Array.Sort(intensitySortBuffer); // in place introspective sort
                            }
                        }
                        stopwatch.Stop();
                        sortTimings.IntrospectiveZ[sortSizeIndex] += stopwatch.Elapsed;

                        // introspective intensity sort
                        for (int warmupIteration = 0; warmupIteration < this.WarmupIterations; ++warmupIteration)
                        {
                            for (int sourceIndex = 0; sourceIndex <= maxSortIndexInclusive; sourceIndex += sortSize)
                            {
                                Array.Copy(intensityValues, sourceIndex, intensitySortBuffer, 0, intensitySortBuffer.Length);
                                Array.Sort(intensitySortBuffer); // in place introspective sort
                            }
                        }
                        stopwatch.Restart();
                        for (int iteration = 0; iteration < this.Iterations; ++iteration)
                        {
                            for (int sourceIndex = 0; sourceIndex <= maxSortIndexInclusive; sourceIndex += sortSize)
                            {
                                Array.Copy(intensityValues, sourceIndex, intensitySortBuffer, 0, intensitySortBuffer.Length);
                                Array.Sort(intensitySortBuffer); // in place introspective sort
                            }
                        }
                        stopwatch.Stop();
                        sortTimings.IntrospectiveIntensity[sortSizeIndex] += stopwatch.Elapsed;

                        // radix intensity sort
                        for (int warmupIteration = 0; warmupIteration <= this.WarmupIterations; ++warmupIteration)
                        {
                            for (int sourceIndex = 0; sourceIndex <= maxSortIndexInclusive; sourceIndex += sortSize)
                            {
                                Array.Copy(intensityValues, sourceIndex, intensitySortBuffer, 0, intensitySortBuffer.Length);
                                SpanExtensions.SortRadix256(intensitySortBuffer, intensityRadixWorkingBuffer);
                            }
                        }
                        stopwatch.Restart();
                        for (int iteration = 0; iteration <= this.Iterations; ++iteration)
                        {
                            for (int sourceIndex = 0; sourceIndex <= maxSortIndexInclusive; sourceIndex += sortSize)
                            {
                                Array.Copy(intensityValues, sourceIndex, intensitySortBuffer, 0, intensitySortBuffer.Length);
                                SpanExtensions.SortRadix256(intensitySortBuffer, intensityRadixWorkingBuffer);
                            }
                        }
                        stopwatch.Stop();
                        sortTimings.RadixIntensity[sortSizeIndex] += stopwatch.Elapsed;

                        int sorts = (int)((long)this.Iterations * (long)acceptedPoints / (long)sortSize);
                        sortTimings.SortCount[sortSizeIndex] += sorts;

                        Interlocked.Increment(ref sortsCompleted);
                    }

                    ++sortTimings.Tiles;
                    sortTimings.AcceptedPoints += acceptedPoints;
                    sortTimings.GridMetricsCells += tilePoints.Cells;
                }

                lock (totalSortTimings)
                {
                    totalSortTimings.Add(sortTimings);
                }
            }, this.cancellationTokenSource);

            int activeReadThreads = readThreads - readSemaphore.CurrentCount;
            TimedProgressRecord sortProgress = new(cmdletName, sortsCompleted + " of " + totalSorts + " sort sizes on " + lasGrid.NonNullCells + (lasGrid.NonNullCells > 1 ? " point clouds..." : " point cloud..."));
            while (sortTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                activeReadThreads = readThreads - readSemaphore.CurrentCount;
                sortProgress.StatusDescription = sortsCompleted + " of " + totalSorts + " sort sizes on " + lasGrid.NonNullCells + (lasGrid.NonNullCells > 1 ? " point clouds..." : " point cloud...");
                sortProgress.Update(sortsCompleted, totalSorts);
                this.WriteProgress(sortProgress);
            }

            this.WriteObject(totalSortTimings);
            sortProgress.Stopwatch.Stop();
            this.WriteVerbose("Timed " + sortsCompleted + " sort sizes over " + lasGrid.NonNullCells + (lasGrid.NonNullCells > 1 ? " point clouds in " : " point cloud in ") + sortProgress.Stopwatch.ToElapsedString() + ": " + totalSortTimings.AcceptedPoints.ToString("n0") + " points in " + totalSortTimings.GridMetricsCells.ToString("n0") + " cells (" + ((float)totalSortTimings.AcceptedPoints / (float)totalSortTimings.GridMetricsCells).ToString("0") + " points/cell).");
            base.ProcessRecord();
        }

        protected override void StopProcessing()
        {
            base.StopProcessing();
            this.cancellationTokenSource.Cancel();
        }

        public class SortTimings
        {
            public int GridMetricsCells { get; set; }
            public int AcceptedPoints { get; set; }
            public int Tiles { get; set; }
            public TimeSpan[] IntrospectiveIntensity { get; private init; }
            public TimeSpan[] IntrospectiveZ { get; private init; }
            public TimeSpan[] RadixIntensity { get; private init; }
            public int[] SortCount { get; private init; }
            public List<int> SortSize { get; private init; }

            public SortTimings(List<int> sortSizes)
            {
                this.AcceptedPoints = 0;
                this.GridMetricsCells = 0;
                this.Tiles = 0;
                this.IntrospectiveIntensity = new TimeSpan[sortSizes.Count]; // leave at zero
                this.IntrospectiveZ = new TimeSpan[sortSizes.Count]; // leave at zero
                this.RadixIntensity = new TimeSpan[sortSizes.Count]; // leave at zero
                this.SortCount = new int[sortSizes.Count]; // leave at zero
                this.SortSize = sortSizes;
            }

            public void Add(SortTimings other)
            {
                if ((Object.ReferenceEquals(this.SortSize, other.SortSize) == false) || (this.SortCount.Length != other.SortCount.Length) || (this.IntrospectiveIntensity.Length != other.IntrospectiveIntensity.Length) || (this.RadixIntensity.Length != other.RadixIntensity.Length))
                {
                    throw new ArgumentOutOfRangeException(nameof(other));
                }

                this.AcceptedPoints += other.AcceptedPoints;
                this.GridMetricsCells += other.GridMetricsCells;
                this.Tiles += other.Tiles;

                for (int sizeIndex = 0; sizeIndex < this.SortCount.Length; ++sizeIndex)
                {
                    if (this.SortSize[sizeIndex] != other.SortSize[sizeIndex])
                    {
                        throw new ArgumentOutOfRangeException(nameof(other));
                    }
                    this.SortCount[sizeIndex] += other.SortCount[sizeIndex];
                }
                for (int sizeIndex = 0; sizeIndex < this.IntrospectiveZ.Length; ++sizeIndex)
                {
                    this.IntrospectiveZ[sizeIndex] += other.IntrospectiveZ[sizeIndex];
                }
                for (int sizeIndex = 0; sizeIndex < this.IntrospectiveIntensity.Length; ++sizeIndex)
                {
                    this.IntrospectiveIntensity[sizeIndex] += other.IntrospectiveIntensity[sizeIndex];
                }
                for (int sizeIndex = 0; sizeIndex < this.RadixIntensity.Length; ++sizeIndex)
                {
                    this.RadixIntensity[sizeIndex] += other.RadixIntensity[sizeIndex];
                }
            }
        }
    }
}
