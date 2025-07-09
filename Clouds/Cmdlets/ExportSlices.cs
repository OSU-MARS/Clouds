using Mars.Clouds.Cmdlets.Hardware;
using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    // TODO: point cloud trimming support? .vrt support?
    [Cmdlet(VerbsData.Export, "Slices")]
    public class ExportSlices : GdalCmdlet
    {
        [Parameter(Mandatory = true, Position = 0, HelpMessage = ".las files to extract slices from. Can be a single file or a set of files if wildcards are used.")]
        [ValidateNotNullOrWhiteSpace]
        public List<string> Las { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "Either a path to a single DTM underlying all of the point clouds specified by -Las or a path to a directory containing individual GeoTIFF DTMs whose file names match the point clouds. DTMs must be single precision floating point rasters with ground surface heights in the same CRS as its correspoinding point cloud tile. The read capability of the DTMs' storage is assumed to be the same as or greater than the DSM's storage.")]
        [ValidateNotNullOrWhiteSpace]
        public string Dtm { get; set; }

        [Parameter(HelpMessage = "Path of output slices. Can be a directory or, in the case of a single file, a file path. Default is empty, in which case slices are written next to their source point clouds.")]
        [ValidateNotNullOrWhiteSpace]
        public string Slice { get; set; }

        [Parameter(HelpMessage = "Name of DTM band to use in calculating ground elevations. Default is the first band.")]
        [ValidateNotNullOrWhiteSpace]
        public string? DtmBand { get; set; }

        [Parameter(HelpMessage = "Cell size of raster slices in point cloud and DTM units. Default is 5 cm for metric inputs and 2.0 inches for English inputs.")]
        [ValidateRange(-100.0, 400.0)] // sanity bounds
        public double CellSize { get; set; }

        [Parameter(HelpMessage = "Minimum height of slice to extract in point cloud and DTM units. Default is 1.37 m for metric inputs and 4.5 feet for English inputs.")]
        [ValidateRange(-100.0, 400.0)] // sanity bounds
        public double MinHeight { get; set; }

        [Parameter(HelpMessage = "Maximum height of slice to extract in point cloud and DTM units. Default is 2.0 m for metric inputs and 6.0 feet for English inputs.")]
        [ValidateRange(-100.0, 400.0)] // sanity bounds
        public double MaxHeight { get; set; }

        // ability to trim at rotations to the coordinate system or follow point shape and density would likely be very helpful in some
        // cases but for now this is just a simple and quick implementation
        [Parameter(HelpMessage = "Distance to shrink point clouds' bounding boxes by when generating slices. Default is zero, meaning every point in the cloud is a candidate for slice inclusion. Values in the vicinity of 20-80 m are often useful for excluding outer hits in handheld LiDAR scans, depending on scanner range, scan pattern, area of interest, coordinate system alignment, and extent to which the point cloud's bounding box is padded.")]
        [ValidateRange(0.0, 500.0)] // sanity upper bound
        public double Trim { get; set; }

        [Parameter(HelpMessage = "Turn off writing of output rasters. This is useful in certain benchmarking and development situations.")]
        public SwitchParameter NoWrite { get; set; }

        [Parameter(HelpMessage = "Whether or not to compress slice rasters. Default is false.")]
        public SwitchParameter CompressRasters { get; set; }

        public ExportSlices() 
        {
            this.CellSize = Double.NaN;
            this.CompressRasters = false;
            this.Dtm = String.Empty;
            this.DtmBand = null;
            this.Las = [];
            this.MaxHeight = Double.NaN;
            this.MinHeight = Double.NaN;
            this.Slice = String.Empty;
            this.Trim = 0.0;
        }

        protected override void ProcessRecord()
        {
            if (this.MaxHeight <= this.MinHeight)
            {
                throw new ParameterOutOfRangeException(nameof(this.MaxHeight), "Maximum slice height " + this.MaxHeight + " is less than or equal to the minimum height " + this.MinHeight + ", indicating a slice whose thickness is negative or zero. -" + nameof(this.MaxHeight) + " must be greater than -" + nameof(this.MinHeight) + " so the slice has a positive thickness and thus can contain points.");
            }

            List<string> cloudPaths = this.GetExistingFilePaths(this.Las, Constant.File.LasExtension);
            bool dtmPathIsDirectory = Directory.Exists(this.Dtm);
            RasterBand<float>? singleDtmBand = null;
            if (dtmPathIsDirectory == false)
            {
                using Dataset singleDtmDataset = Gdal.Open(this.Dtm, Access.GA_ReadOnly);
                Raster<float> singleDtm = new(singleDtmDataset, readData: true);
                singleDtmBand = singleDtm.GetBand(this.DtmBand);
            }

            bool slicePathSet = String.IsNullOrWhiteSpace(this.Slice) == false;
            bool slicePathIsDirectory = slicePathSet && Directory.Exists(this.Slice);

            (float driveTransferRateSingleThreadInGBs, float ddrBandwidthSingleThreadInGBs) = LasReader.GetPointsToImageBandwidth(unbuffered: false);
            int readThreads = Int32.Min(HardwareCapabilities.Current.GetPracticalReadThreadCount(this.Las, this.SessionState.Path.CurrentLocation.Path, driveTransferRateSingleThreadInGBs, ddrBandwidthSingleThreadInGBs), this.DataThreads);

            int slicesInitiated = -1;
            int slicesWritten = 0;
            ParallelTasks cloudRegistrationTasks = new(Int32.Min(readThreads, cloudPaths.Count), () =>
            {
                byte[]? pointReadBuffer = null;
                for (int cloudIndex = Interlocked.Increment(ref slicesInitiated); cloudIndex < cloudPaths.Count; cloudIndex = Interlocked.Increment(ref slicesInitiated))
                {
                    // load cloud and its DTM
                    string cloudPath = cloudPaths[cloudIndex];
                    using LasReader reader = LasReader.CreateForPointRead(cloudPath);
                    LasFile cloud = new(reader, fallbackCreationDate: null);
                    SpatialReference cloudCrs = cloud.GetSpatialReference();

                    string cloudName = Tile.GetName(cloudPath);
                    RasterBand<float> dtmBand;
                    if (dtmPathIsDirectory)
                    {
                        string dtmPath = Path.Combine(this.Dtm, cloudName + Constant.File.GeoTiffExtension);
                        using Dataset dtmDataset = Gdal.Open(this.Dtm, Access.GA_ReadOnly);
                        Raster<float> dtm = new(dtmDataset, readData: true);
                        dtmBand = dtm.GetBand(this.DtmBand);
                    }
                    else
                    {
                        Debug.Assert(singleDtmBand != null);
                        dtmBand = singleDtmBand;
                    }

                    if (SpatialReferenceExtensions.IsSameCrs(dtmBand.Crs, cloudCrs) == false)
                    {
                        throw new NotSupportedException("Point cloud '" + cloudPath + "'s coordinate system ('" + cloudCrs.GetName() + "') does not match the DTM's coordinate system ('" + dtmBand.Crs.GetName() + "'). DTM path is '" + (dtmPathIsDirectory ? Path.Combine(this.Dtm, cloudName + Constant.File.GeoTiffExtension) : this.Dtm) + "'.");
                    }

                    double cellSizeInCrsUnits;
                    double maxHeightInCrsUnits;
                    double minHeightInCrsUnits;
                    if (cloudCrs.GetLinearUnits() == Constant.Gdal.LinearUnitMeter)
                    {
                        cellSizeInCrsUnits = Double.IsNaN(this.CellSize) ? 0.05F : this.CellSize; // 5 cm
                        maxHeightInCrsUnits = Double.IsNaN(this.MaxHeight) ? 2.0F : this.MaxHeight;
                        minHeightInCrsUnits = Double.IsNaN(this.MaxHeight) ? 1.37F : this.MinHeight;
                    }
                    else
                    {
                        Debug.Assert(cloudCrs.GetLinearUnits() == Constant.Gdal.LinearUnitFoot);
                        cellSizeInCrsUnits = Double.IsNaN(this.CellSize) ? 2.0F / 12.0F : this.CellSize; // 2.0 inches
                        maxHeightInCrsUnits = Double.IsNaN(this.MaxHeight) ? 6.0F : this.MaxHeight;
                        minHeightInCrsUnits = Double.IsNaN(this.MaxHeight) ? 4.5F : this.MinHeight;
                    }

                    // extract slice
                    IntensitySlice intensitySlice = new(cloud, cellSizeInCrsUnits, this.Trim);
                    reader.ReadIntensitySlice(cloud, intensitySlice, dtmBand, minHeightInCrsUnits, maxHeightInCrsUnits, this.Trim, ref pointReadBuffer);

                    if (this.NoWrite == false)
                    {
                        string sliceName = cloudName + " slice " + minHeightInCrsUnits.ToString("0.0##") + "-" + maxHeightInCrsUnits.ToString("0.0##") + " " + cloudCrs.GetVerticalLinearUnitName() + (this.Trim > 0 ? " trim " + this.Trim.ToString("0") : String.Empty) + Constant.File.GeoTiffExtension;
                        string slicePath;
                        if (slicePathSet)
                        {
                            slicePath = slicePathIsDirectory ? Path.Combine(this.Slice, sliceName) : this.Slice;
                        }
                        else
                        {
                            string? cloudDirectoryPath = Path.GetDirectoryName(cloudPath);
                            slicePath = cloudDirectoryPath != null ? Path.Combine(cloudDirectoryPath, sliceName) : sliceName;
                        }
                        intensitySlice.WriteMean(slicePath, this.CompressRasters);
                    }

                    Interlocked.Increment(ref slicesWritten);
                    if (this.Stopping || this.CancellationTokenSource.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }, this.CancellationTokenSource);

            TimedProgressRecord progress = new("Export-Slices", "Sliced " + slicesWritten + " of " + cloudPaths.Count + " point clouds...");
            while (cloudRegistrationTasks.WaitAll(Constant.DefaultProgressInterval) == false)
            {
                progress.StatusDescription = "Sliced " + slicesWritten + " of " + cloudPaths.Count + " point clouds...";
                progress.Update(slicesWritten, cloudPaths.Count);
                this.WriteProgress(progress);
            }

            base.ProcessRecord();
        }
    }
}
