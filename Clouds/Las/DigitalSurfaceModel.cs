using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Mars.Clouds.Las
{
    public class DigitalSurfaceModel : Raster, IRasterSerializable<DigitalSurfaceModel>
    {
        private const string DiagnosticDirectoryPointCounts = "nPoints";
        private const string DiagnosticDirectoryReturnNumber = "returnNumber";
        private const string DiagnosticDirectorySourceID = "sourceID";
        private const string DiagnosticDirectoryZ = "z";

        public const string AerialPointsBandName = "nAerial";
        public const string CanopyHeightBandName = "chm";
        public const string CanopyMaximaBandName = "cmm3";
        public const string SurfaceBandName = "dsm";
        public const string SubsurfaceBandName = "subsurfaceDsm";
        public const string AerialMeanBandName = "aerialMean";
        public const string GroundMeanBandName = "groundMean";
        public const string GroundPointsBandName = "nGround";
        public const string ReturnNumberBandName = "returnNumberSurface";
        public const string SourceIDSurfaceBandName = "sourceIDsurface";
        public const int SubsurfaceBufferDepth = 8; // half a cache line per DSM cell

        // primary data bands
        // Digital terrain model can be calculated as DTM = DSM - CHM = Surface - CanopyHeight or stored separately.
        public RasterBand<float> Surface { get; private set; } // digital surface model
        public RasterBand<float> CanopyMaxima3 { get; private set; } // canopy maxima model obtained from the digital surface model using a 3x3 kernel
        public RasterBand<float> CanopyHeight { get; private set; } // canopy height model obtained from DSM - DTM

        // diagnostic bands in z
        public RasterBand<float>? Subsurface { get; private set; } // estimate of next surface layer below digital surface model
        public RasterBand<float>? AerialMean { get; private set; } // mean elevation of aerial points in cell
        public RasterBand<float>? GroundMean { get; private set; } // mean elevation of ground points in cell

        // diagnostic bands: point counts
        public RasterBand<UInt32>? AerialPoints { get; private set; } // number of points in cell not classified as ground
        public RasterBand<UInt32>? GroundPoints { get; private set; } // number of ground points in cell

        // diagnostic bands: return number
        public RasterBand<byte>? ReturnNumberSurface { get; private set; }

        // diagnostic bands: source IDs
        public RasterBand<UInt16>? SourceIDSurface { get; private set; }

        public DigitalSurfaceModel(string dsmFilePath, LasFile lasFile, DigitalSufaceModelBands bands, RasterBand<float> dtmTile)
            : base(lasFile.GetSpatialReference(), dtmTile.Transform, dtmTile.SizeX, dtmTile.SizeY)
        {
            if ((bands & DigitalSufaceModelBands.Required) != DigitalSufaceModelBands.Required)
            {
                throw new ArgumentOutOfRangeException(nameof(bands), "One or more required bands is missing from " + nameof(bands) + ".");
            }
            if (this.Crs.IsCompound() == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lasFile), dsmFilePath + ": point cloud's coordinate reference system (CRS) is not a compound CRS. Both a horizontal and vertical CRS are needed to fully geolocate a digital surface model's elevations.");
            }

            SpatialReference lasTileCrs = lasFile.GetSpatialReference();
            if (SpatialReferenceExtensions.IsSameCrs(lasTileCrs, dtmTile.Crs) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(dtmTile), dsmFilePath + ": point clouds and DTMs are currently required to be in the same CRS. The point cloud CRS is '" + lasTileCrs.GetName() + "' while the DTM CRS is " + dtmTile.Crs.GetName() + ".");
            }

            this.FilePath = dsmFilePath;

            this.Surface = new(this, DigitalSurfaceModel.SurfaceBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData);
            this.CanopyMaxima3 = new(this, DigitalSurfaceModel.CanopyMaximaBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData);
            this.CanopyHeight = new(this, DigitalSurfaceModel.CanopyHeightBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData);

            this.Subsurface = null;
            this.AerialMean = null;
            this.GroundMean = null;
            this.AerialPoints = null;
            this.GroundPoints = null;
            this.ReturnNumberSurface = null;
            this.SourceIDSurface = null;

            if (bands.HasFlag(DigitalSufaceModelBands.Subsurface))
            {
                this.Subsurface = new(this, DigitalSurfaceModel.SubsurfaceBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData);
            }
            if (bands.HasFlag(DigitalSufaceModelBands.AerialMean))
            {
                this.AerialMean = new(this, DigitalSurfaceModel.AerialMeanBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData);
            }
            if (bands.HasFlag(DigitalSufaceModelBands.GroundMean))
            {
                this.GroundMean = new(this, DigitalSurfaceModel.GroundMeanBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData);
            }
            if (bands.HasFlag(DigitalSufaceModelBands.AerialPoints))
            {
                this.AerialPoints = new(this, DigitalSurfaceModel.AerialPointsBandName, RasterBandInitialValue.Default); // leave at default of zero, lacks no data value as count of zero is valid
            }
            if (bands.HasFlag(DigitalSufaceModelBands.GroundPoints))
            {
                this.GroundPoints = new(this, DigitalSurfaceModel.GroundPointsBandName, RasterBandInitialValue.Default); // leave at default of zero, lacks no data value as count of zero is valid
            }
            if (bands.HasFlag(DigitalSufaceModelBands.ReturnNumberSurface))
            {
                this.ReturnNumberSurface = new(this, DigitalSurfaceModel.ReturnNumberBandName, 0, RasterBandInitialValue.Default); // leave at default of zero, which is defined as no data in LAS specification
            }
            if (bands.HasFlag(DigitalSufaceModelBands.SourceIDSurface))
            {
                this.SourceIDSurface = new(this, DigitalSurfaceModel.SourceIDSurfaceBandName, 0, RasterBandInitialValue.NoData); // set no data to zero and leave at default of zero as LAS spec defines source IDs 1-65535 as valid
            }
        }

        public DigitalSurfaceModel(Dataset dsmDataset, bool readData)
            : base(dsmDataset)
        {
            for (int gdalBandIndex = 1; gdalBandIndex <= dsmDataset.RasterCount; ++gdalBandIndex)
            {
                using Band gdalBand = dsmDataset.GetRasterBand(gdalBandIndex);
                string bandName = gdalBand.GetDescription();
                switch (bandName)
                {
                    case DigitalSurfaceModel.SurfaceBandName:
                        this.Surface = new(dsmDataset, gdalBand, readData);
                        break;
                    case DigitalSurfaceModel.CanopyMaximaBandName:
                        this.CanopyMaxima3 = new(dsmDataset, gdalBand, readData);
                        break;
                    case DigitalSurfaceModel.CanopyHeightBandName:
                        this.CanopyHeight = new(dsmDataset, gdalBand, readData);
                        break;
                    // only required bands are expected in the primary dataset but it's possible diagnostic bands have been merged
                    case DigitalSurfaceModel.SubsurfaceBandName:
                        this.Subsurface = new(dsmDataset, gdalBand, readData);
                        break;
                    case DigitalSurfaceModel.AerialMeanBandName:
                        this.AerialMean = new(dsmDataset, gdalBand, readData);
                        break;
                    case DigitalSurfaceModel.GroundMeanBandName:
                        this.GroundMean = new(dsmDataset, gdalBand, readData);
                        break;
                    case DigitalSurfaceModel.AerialPointsBandName:
                        this.AerialPoints = new(dsmDataset, gdalBand, readData);
                        break;
                    case DigitalSurfaceModel.GroundPointsBandName:
                        this.GroundPoints = new(dsmDataset, gdalBand, readData);
                        break;
                    case DigitalSurfaceModel.ReturnNumberBandName:
                        this.ReturnNumberSurface = new(dsmDataset, gdalBand, readData);
                        break;
                    case DigitalSurfaceModel.SourceIDSurfaceBandName:
                        this.SourceIDSurface = new(dsmDataset, gdalBand, readData);
                        break;
                    default:
                        throw new NotSupportedException("Unhandled band '" + bandName + "'.");
                }
            }
            
            // required bands
            if (this.Surface == null)
            {
                throw new ArgumentOutOfRangeException(nameof(dsmDataset), "DSM raster does not contain a band named 'dsm'.");
            }
            if (this.CanopyMaxima3 == null)
            {
                throw new ArgumentOutOfRangeException(nameof(dsmDataset), "DSM raster does not contain a band named 'cmm3'.");
            }
            if (this.CanopyHeight == null)
            {
                throw new ArgumentOutOfRangeException(nameof(dsmDataset), "DSM raster does not contain a band named 'chm'.");
            }

            // other bands are optional and can remain null
        }

        public override IEnumerable<RasterBand> GetBands()
        {
            yield return this.Surface;
            yield return this.CanopyMaxima3;
            yield return this.CanopyHeight;

            if (this.Subsurface != null)
            {
                yield return this.Subsurface;
            }
            if (this.AerialMean != null)
            {
                yield return this.AerialMean;
            }
            if (this.GroundMean != null)
            {
                yield return this.GroundMean;
            }

            if (this.AerialPoints != null)
            {
                yield return this.AerialPoints;
            }
            if (this.GroundPoints != null)
            {
                yield return this.GroundPoints;
            }

            if (this.ReturnNumberSurface != null)
            {
                yield return this.ReturnNumberSurface;
            }
            if (this.SourceIDSurface != null)
            {
                yield return this.SourceIDSurface;
            }
        }

        public override int GetBandIndex(string name)
        {
            if (String.Equals(name, this.Surface.Name, StringComparison.Ordinal))
            {
                return 0;
            }
            if (String.Equals(name, this.CanopyMaxima3.Name, StringComparison.Ordinal))
            {
                return 1;
            }
            if (String.Equals(name, this.CanopyHeight.Name, StringComparison.Ordinal))
            {
                return 2;
            }

            int bandIndex = 3;
            if (this.Subsurface != null)
            {
                if (String.Equals(name, this.Subsurface.Name, StringComparison.Ordinal))
                {
                    return bandIndex;
                }
                ++bandIndex;
            }
            if (this.AerialMean != null)
            {
                if (String.Equals(name, this.AerialMean.Name, StringComparison.Ordinal))
                {
                    return bandIndex;
                }
                ++bandIndex;
            }
            if (this.GroundMean != null)
            {
                if (String.Equals(name, this.GroundMean.Name, StringComparison.Ordinal))
                {
                    return bandIndex;
                }
                ++bandIndex;
            }

            if (this.AerialPoints != null)
            {
                if (String.Equals(name, this.AerialPoints.Name, StringComparison.Ordinal))
                {
                    return bandIndex;
                }
                ++bandIndex;
            }
            if (this.GroundPoints != null)
            {
                if (String.Equals(name, this.GroundPoints.Name, StringComparison.Ordinal))
                {
                    return bandIndex;
                }
                ++bandIndex;
            }

            if (this.ReturnNumberSurface != null)
            {
                if (String.Equals(name, this.ReturnNumberSurface.Name, StringComparison.Ordinal))
                {
                    return bandIndex;
                }
                ++bandIndex;
            }
            if (this.SourceIDSurface != null)
            {
                if (String.Equals(name, this.SourceIDSurface.Name, StringComparison.Ordinal))
                {
                    return bandIndex;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(name), "No band named '" + name + "' found in raster.");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MaybeInsertSubsurfacePoint(int cellIndex, float dsmZ, int aerialPoints, float z, float[] subsurfaceBuffer)
        {
            float subsurfaceCandidateZ = z;
            if (dsmZ < z)
            {
                // point lifts DSM so current DSM point moves to first subsurface point
                // first subsurface point becuomes buffer candidate point
                subsurfaceCandidateZ = this.Subsurface![cellIndex];
                this.Subsurface![cellIndex] = dsmZ;
            }
            else if (aerialPoints == 1)
            {
                // DSM has been established but first subsurface point hasn't been set
                this.Subsurface![cellIndex] = z;
            }

            if (aerialPoints > 1)
            {
                // both DSM and subsurface points are set; does subsurface candidate point ripple into buffer?
                float subsurfaceZ = this.Subsurface![cellIndex];
                if (subsurfaceZ < subsurfaceCandidateZ)
                {
                    // candidate point becomes first subsurface position and current subsurface becomes buffer candidate
                    this.Subsurface![cellIndex] = subsurfaceCandidateZ;
                    subsurfaceCandidateZ = subsurfaceZ;
                }

                Span<float> cellSubsurfaceBuffer = subsurfaceBuffer.AsSpan().Slice(DigitalSurfaceModel.SubsurfaceBufferDepth * (int)cellIndex, DigitalSurfaceModel.SubsurfaceBufferDepth);
                int aeriaPointsAvailableToCellSubsurfaceBuffer = (int)aerialPoints - 2;
                if (aeriaPointsAvailableToCellSubsurfaceBuffer >= DigitalSurfaceModel.SubsurfaceBufferDepth)
                {
                    // subsurface buffer is full: if point is to be inserted another must be evicted
                    for (int cellSubsurfaceBufferIndex = 0; cellSubsurfaceBufferIndex < DigitalSurfaceModel.SubsurfaceBufferDepth; ++cellSubsurfaceBufferIndex)
                    {
                        float subsurfaceBufferZ = cellSubsurfaceBuffer![cellSubsurfaceBufferIndex];
                        if (subsurfaceBufferZ > subsurfaceCandidateZ)
                        {
                            cellSubsurfaceBuffer![cellSubsurfaceBufferIndex] = subsurfaceCandidateZ;
                            for (int moveDestinationIndex = cellSubsurfaceBufferIndex + 1; moveDestinationIndex < DigitalSurfaceModel.SubsurfaceBufferDepth; ++moveDestinationIndex)
                            {
                                (subsurfaceBufferZ, cellSubsurfaceBuffer[moveDestinationIndex]) = (cellSubsurfaceBuffer[moveDestinationIndex], subsurfaceBufferZ);
                            }

                            return;
                        }
                    }
                }
                else
                {
                    // insert point into subsurface buffer in ascending order
                    int pointsInCellSubsurfaceBuffer = aeriaPointsAvailableToCellSubsurfaceBuffer;
                    for (int cellSubsurfaceBufferIndex = 0; cellSubsurfaceBufferIndex <= pointsInCellSubsurfaceBuffer; ++cellSubsurfaceBufferIndex)
                    {
                        float subsurfaceBufferZ = cellSubsurfaceBuffer![cellSubsurfaceBufferIndex];
                        if (subsurfaceBufferZ < subsurfaceCandidateZ)
                        {
                            cellSubsurfaceBuffer![cellSubsurfaceBufferIndex] = subsurfaceCandidateZ;
                            for (int moveDestinationIndex = cellSubsurfaceBufferIndex + 1; moveDestinationIndex <= pointsInCellSubsurfaceBuffer; ++moveDestinationIndex)
                            {
                                (subsurfaceBufferZ, cellSubsurfaceBuffer[moveDestinationIndex]) = (cellSubsurfaceBuffer[moveDestinationIndex], subsurfaceBufferZ);
                            }

                            return;
                        }
                    }
                }
            }
        }

        // TODO: SIMD
        public void OnPointAdditionComplete(RasterBand<float> dtm, float subsurfaceGapDistance, float[]? subsurfaceBuffer)
        {
            bool hasSubsurface = (this.Subsurface != null) && (subsurfaceBuffer != null);
            if (hasSubsurface && (this.AerialPoints == null))
            {
                throw new NotSupportedException("Aerial point count is required to adjust subsurface z values.");
            }

            if (SpatialReferenceExtensions.IsSameCrs(this.Crs, dtm.Crs) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(dtm), "DSMs and DTMs are currently required to be in the same CRS. The DSM CRS is '" + this.Crs.GetName() + "' while the DTM CRS is " + dtm.Crs.GetName() + ".");
            }
            if (this.IsSameExtentAndSpatialResolution(dtm) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(dtm), "DTM extent (" + dtm.GetExtentString() + ") or size (" + dtm.SizeX + " x " + dtm.SizeY + ") does not match DSM extent (" + this.GetExtentString() + ") or size (" + this.SizeX + " by " + this.SizeY + ").");
            }

            // nothing to do for this.Surface
            // for now, population of the canopy maxima model is deferred until a virtual raster neighborhood is available
            // Binomial.Smooth3x3(this.Surface, this.CanopyMaxima3);

            // set canopy height and, if available, adjust subsurface based on subsurface buffer
            for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
            {
                float dsmZ = this.Surface[cellIndex];
                // canopy height is calculated relative to DTM
                // If needed, mean ground elevation can be used instead.
                this.CanopyHeight[cellIndex] = dsmZ - dtm[cellIndex];

                if (hasSubsurface) // if there's no subsurface buffer nothing to do but leave this.Subsurface as is
                {
                    float subsurfaceGappedZ = dsmZ - subsurfaceGapDistance;
                    float subsurfaceZ = this.Subsurface![cellIndex]; // nothing to do if this.Subsurface satisifes gap
                    if (subsurfaceZ > subsurfaceGappedZ)
                    {
                        int cellSubsurfacePoints = (int)this.AerialPoints![cellIndex] - 2;
                        if (cellSubsurfacePoints > DigitalSurfaceModel.SubsurfaceBufferDepth)
                        {
                            cellSubsurfacePoints = DigitalSurfaceModel.SubsurfaceBufferDepth;
                        }

                        Span<float> cellSubsurfaceBuffer = subsurfaceBuffer.AsSpan().Slice(DigitalSurfaceModel.SubsurfaceBufferDepth * (int)cellIndex, DigitalSurfaceModel.SubsurfaceBufferDepth);
                        bool subsurfaceGapFound = false;
                        for (int cellSubsurfaceIndex = cellSubsurfacePoints - 1; cellSubsurfaceIndex > 0; --cellSubsurfaceIndex)
                        {
                            subsurfaceZ = cellSubsurfaceBuffer[cellSubsurfaceIndex];
                            if (subsurfaceZ < subsurfaceGappedZ)
                            {
                                this.Subsurface[cellIndex] = subsurfaceZ;
                                subsurfaceGapFound = true;
                                break;
                            }
                        }

                        if (subsurfaceGapFound == false)
                        {
                            this.Subsurface[cellIndex] = cellSubsurfaceBuffer[0];
                        }
                    }
                }
            }

            // find mean elevations of aerial and ground points
            if (this.AerialMean != null)
            {
                if (this.AerialPoints == null)
                {
                    throw new NotSupportedException("Mean z of aerial points cannot be calculated without the aerial point count.");
                }
                for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
                {
                    UInt32 nAerial = this.AerialPoints[cellIndex];
                    if (nAerial > 0)
                    {
                        this.AerialMean[cellIndex] /= nAerial;
                    }
                    // otherwise leave mean aerial elevation as no data
                }
            }
            if (this.GroundMean != null)
            {
                if (this.GroundPoints == null)
                {
                    throw new NotSupportedException("Mean z of ground points cannot be calculated without the ground point count.");
                }
                for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
                {
                    UInt32 nGround = this.GroundPoints[cellIndex];
                    if (nGround > 0)
                    {
                        this.GroundMean[cellIndex] /= nGround;
                    }
                    // otherwise leave ground elevation as no data
                }
            }

            // nothing to do for
            // this.ReturnNumber
            // this.SourceIDSurface
        }

        /// <summary>
        /// Read specified digital surface model bands. Required bands (<see cref="DigitalSufaceModelBands.Required"/>) must always be requested.
        /// </summary>
        /// <remarks>
        /// For now, does not modify bands whose flags are not set. They will be left as null if never loaded or with previous data if
        /// previously loaded.
        /// </remarks>
        public void Read(DigitalSufaceModelBands bands, ObjectPool<DigitalSurfaceModel> tilePool)
        {
            if ((bands & DigitalSufaceModelBands.Required) != DigitalSufaceModelBands.Required)
            {
                throw new ArgumentNullException(nameof(bands), "Required bands must be set on " + bands + ".");
            }

            // alleviate memory pressure by offloading GC in streaming virtual raster reads
            // If a previously used tile has been returned into the object pool, capture its required band data arrays and any
            // unpopulated diagnostic bands. Not very useful if tile sizes depart from the virtual raster convention of all being
            // identical size.
            if (tilePool.TryGetThreadSafe(out DigitalSurfaceModel? unusedTile))
            {
                this.Surface.TakeOwnershipOfDataArray(unusedTile.Surface);
                this.CanopyMaxima3.TakeOwnershipOfDataArray(unusedTile.CanopyMaxima3);
                this.CanopyHeight.TakeOwnershipOfDataArray(unusedTile.CanopyHeight);

                if ((this.Subsurface == null) && (unusedTile.Subsurface != null))
                {
                    this.Subsurface = unusedTile.Subsurface;
                    unusedTile.Subsurface = null;
                }
                if ((this.AerialMean == null) && (unusedTile.AerialMean != null))
                {
                    this.AerialMean = unusedTile.AerialMean;
                    unusedTile.AerialMean = null;
                }
                if ((this.GroundMean == null) && (unusedTile.GroundMean != null))
                {
                    this.GroundMean = unusedTile.GroundMean;
                    unusedTile.GroundMean = null;
                }

                if ((this.AerialPoints == null) && (unusedTile.AerialPoints != null))
                {
                    this.AerialPoints = unusedTile.AerialPoints;
                    unusedTile.AerialPoints = null;
                }
                if ((this.GroundPoints == null) && (unusedTile.GroundPoints != null))
                {
                    this.GroundPoints = unusedTile.GroundPoints;
                    unusedTile.GroundPoints = null;
                }

                if ((this.ReturnNumberSurface == null) && (unusedTile.ReturnNumberSurface != null))
                {
                    this.ReturnNumberSurface = unusedTile.ReturnNumberSurface;
                    unusedTile.SourceIDSurface = null;
                }
                if ((this.SourceIDSurface == null) && (unusedTile.SourceIDSurface != null))
                {
                    this.SourceIDSurface = unusedTile.SourceIDSurface;
                    unusedTile.SourceIDSurface = null;
                }

                // rest of unused tile is discarded
                // Raster and raster band parts should be well under 85 kB and thus on the GC's small object heaps.
            }

            using Dataset dsmDataset = Gdal.Open(this.FilePath, Access.GA_ReadOnly);
            Debug.Assert((this.SizeX == dsmDataset.RasterXSize) && (this.SizeY == dsmDataset.RasterYSize) && SpatialReferenceExtensions.IsSameCrs(this.Crs, dsmDataset.GetSpatialRef()));
            for (int gdalBandIndex = 1; gdalBandIndex <= dsmDataset.RasterCount; ++gdalBandIndex)
            {
                Band gdalBand = dsmDataset.GetRasterBand(gdalBandIndex);
                string bandName = gdalBand.GetDescription();
                switch (bandName)
                {
                    case DigitalSurfaceModel.SurfaceBandName:
                        Debug.Assert(this.Surface.IsNoData(gdalBand));
                        this.Surface.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case DigitalSurfaceModel.CanopyMaximaBandName:
                        Debug.Assert(this.CanopyMaxima3.IsNoData(gdalBand));
                        this.CanopyMaxima3.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    case DigitalSurfaceModel.CanopyHeightBandName:
                        Debug.Assert(this.CanopyHeight.IsNoData(gdalBand));
                        this.CanopyHeight.ReadDataAssumingSameCrsTransformSizeAndNoData(gdalBand);
                        break;
                    default:
                        throw new NotSupportedException("Unhandled band '" + bandName + "' in DSM tile '" + this.FilePath + ".");
                }
            }

            if ((bands & DigitalSufaceModelBands.DiagnosticZ) != DigitalSufaceModelBands.None)
            {
                string zTilePath = Raster.GetDiagnosticFilePath(this.FilePath, DigitalSurfaceModel.DiagnosticDirectoryZ, createDiagnosticDirectory: true);
                using Dataset zDataset = Gdal.Open(zTilePath, Access.GA_ReadOnly);
                SpatialReference crs = zDataset.GetSpatialRef();
                for (int gdalBandIndex = 1; gdalBandIndex <= zDataset.RasterCount; ++gdalBandIndex)
                {
                    Band gdalBand = zDataset.GetRasterBand(gdalBandIndex);
                    string bandName = gdalBand.GetDescription();
                    switch (bandName)
                    {
                        case DigitalSurfaceModel.SubsurfaceBandName:
                            if (bands.HasFlag(DigitalSufaceModelBands.Subsurface))
                            {
                                if (this.Subsurface == null)
                                {
                                    this.Subsurface = new(zDataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.Subsurface.Read(zDataset, crs, gdalBand);
                                }
                            }
                            break;
                        case DigitalSurfaceModel.AerialMeanBandName:
                            if (bands.HasFlag(DigitalSufaceModelBands.AerialMean))
                            {
                                if (this.AerialMean == null)
                                {
                                    this.AerialMean = new(zDataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.AerialMean.Read(zDataset, crs, gdalBand);
                                }
                            }
                            break;
                        case DigitalSurfaceModel.GroundMeanBandName:
                            if (bands.HasFlag(DigitalSufaceModelBands.GroundMean))
                            {
                                if (this.GroundMean == null)
                                {
                                    this.GroundMean = new(zDataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.GroundMean.Read(zDataset, crs, gdalBand);
                                }
                            }
                            break;
                        default:
                            throw new NotSupportedException("Unhandled band '" + bandName + "' in z diagnostics tile '" + zTilePath + "'.");
                    }
                }
            }

            if ((bands & DigitalSufaceModelBands.PointCounts) != DigitalSufaceModelBands.None)
            {
                string pointCountTilePath = Raster.GetDiagnosticFilePath(this.FilePath, DigitalSurfaceModel.DiagnosticDirectoryPointCounts, createDiagnosticDirectory: true);
                using Dataset pointCountDataset = Gdal.Open(pointCountTilePath, Access.GA_ReadOnly);
                SpatialReference crs = pointCountDataset.GetSpatialRef();
                for (int gdalBandIndex = 1; gdalBandIndex <= pointCountDataset.RasterCount; ++gdalBandIndex)
                {
                    Band gdalBand = pointCountDataset.GetRasterBand(gdalBandIndex);
                    string bandName = gdalBand.GetDescription();
                    switch (bandName)
                    {
                        case DigitalSurfaceModel.AerialPointsBandName:
                            if (bands.HasFlag(DigitalSufaceModelBands.AerialPoints))
                            {
                                if (this.AerialPoints == null)
                                {
                                    this.AerialPoints = new(pointCountDataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.AerialPoints.Read(pointCountDataset, crs, gdalBand);
                                }
                            }
                            break;
                        case DigitalSurfaceModel.GroundPointsBandName:
                            if (bands.HasFlag(DigitalSufaceModelBands.GroundPoints))
                            {
                                if (this.GroundPoints == null)
                                {
                                    this.GroundPoints = new(pointCountDataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.GroundPoints.Read(pointCountDataset, crs, gdalBand);
                                }
                            }
                            break;
                        default:
                            throw new NotSupportedException("Unhandled band '" + bandName + "' in point count diagnostics tile '" + pointCountTilePath + "'.");
                    }
                }
            }

            if ((bands & DigitalSufaceModelBands.ReturnNumberSurface) != DigitalSufaceModelBands.None)
            {
                string returnNumberTilePath = Raster.GetDiagnosticFilePath(this.FilePath, DigitalSurfaceModel.DiagnosticDirectoryReturnNumber, createDiagnosticDirectory: false);
                using Dataset returnNumberDataset = Gdal.Open(returnNumberTilePath, Access.GA_ReadOnly);
                SpatialReference crs = returnNumberDataset.GetSpatialRef();
                for (int gdalBandIndex = 1; gdalBandIndex <= returnNumberDataset.RasterCount; ++gdalBandIndex)
                {
                    Band gdalBand = returnNumberDataset.GetRasterBand(gdalBandIndex);
                    string bandName = gdalBand.GetDescription();
                    switch (bandName)
                    {
                        case DigitalSurfaceModel.ReturnNumberBandName:
                            if (bands.HasFlag(DigitalSufaceModelBands.ReturnNumberSurface))
                            {
                                if (this.ReturnNumberSurface == null)
                                {
                                    this.ReturnNumberSurface = new(returnNumberDataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.ReturnNumberSurface.Read(returnNumberDataset, crs, gdalBand);
                                }
                            }
                            break;
                        default:
                            throw new NotSupportedException("Unhandled band '" + bandName + "' in source ID diagnostics tile '" + returnNumberTilePath + "'.");
                    }
                }
            }

            if ((bands & DigitalSufaceModelBands.SourceIDSurface) != DigitalSufaceModelBands.None)
            {
                string sourceIDtilePath = Raster.GetDiagnosticFilePath(this.FilePath, DigitalSurfaceModel.DiagnosticDirectorySourceID, createDiagnosticDirectory: false);
                using Dataset sourceIDdataset = Gdal.Open(sourceIDtilePath, Access.GA_ReadOnly);
                SpatialReference crs = sourceIDdataset.GetSpatialRef();
                for (int gdalBandIndex = 1; gdalBandIndex <= sourceIDdataset.RasterCount; ++gdalBandIndex)
                {
                    Band gdalBand = sourceIDdataset.GetRasterBand(gdalBandIndex);
                    string bandName = gdalBand.GetDescription();
                    switch (bandName)
                    {
                        case DigitalSurfaceModel.SourceIDSurfaceBandName:
                            if (bands.HasFlag(DigitalSufaceModelBands.SourceIDSurface))
                            {
                                if (this.SourceIDSurface == null)
                                {
                                    this.SourceIDSurface = new(sourceIDdataset, gdalBand, readData: true);
                                }
                                else
                                {
                                    this.SourceIDSurface.Read(sourceIDdataset, crs, gdalBand);
                                }
                            }
                            break;
                        default:
                            throw new NotSupportedException("Unhandled band '" + bandName + "' in source ID diagnostics tile '" + sourceIDtilePath + "'.");
                    }
                }
            }
        }

        public static DigitalSurfaceModel Read(string rasterPath, bool readData)
        {
            // likely reads only primary bands with diagnostic bands needing calls to Read()'s other overload
            using Dataset dsmDataset = Gdal.Open(rasterPath, Access.GA_ReadOnly);
            return new(dsmDataset, readData);
        }

        public void Reset(string filePath, LasFile lasFile, Grid newExtents)
        {
            // inherited from Grid
            if ((this.SizeX != newExtents.SizeX) || (this.SizeY != newExtents.SizeY))
            {
                throw new NotSupportedException(nameof(this.Reset) + " does not currently support changing the DSM's size from " + this.SizeX + " x " + this.SizeY + " cells to " + newExtents.SizeX + " x " + newExtents.SizeY + ".");
            }

            SpatialReference lasFileCrs = lasFile.GetSpatialReference();
            if (SpatialReferenceExtensions.IsSameCrs(this.Crs, lasFileCrs) == false)
            {
                if (lasFileCrs.IsCompound() == 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(lasFile), filePath + ": point cloud's coordinate reference system (CRS) is not a compound CRS. Both a horizontal and vertical CRS are needed to fully geolocate a digital surface model's elevations.");
                }
                this.Crs = lasFileCrs;
            }
            this.Transform.Copy(newExtents.Transform);

            // inherited from Raster
            this.FilePath = filePath;

            // digital surface model fields
            Debug.Assert(this.Surface.HasNoDataValue && this.CanopyMaxima3.HasNoDataValue && this.CanopyHeight.HasNoDataValue);

            Array.Fill(this.Surface.Data, this.Surface.NoDataValue);
            Array.Fill(this.CanopyMaxima3.Data, this.CanopyMaxima3.NoDataValue);
            Array.Fill(this.CanopyHeight.Data, this.CanopyHeight.NoDataValue);

            if (this.Subsurface != null)
            {
                Array.Fill(this.Subsurface.Data, this.Subsurface.NoDataValue);
            }
            if (this.AerialMean != null)
            {
                Array.Fill(this.AerialMean.Data, this.AerialMean.NoDataValue);
            }
            if (this.GroundMean != null)
            {
                Array.Fill(this.GroundMean.Data, this.GroundMean.NoDataValue);
            }

            if (this.AerialPoints != null)
            {
                Array.Fill(this.AerialPoints.Data, 0U);
            }
            if (this.GroundPoints != null)
            {
                Array.Fill(this.GroundPoints.Data, 0U);
            }

            if (this.ReturnNumberSurface != null)
            {
                Array.Fill(this.ReturnNumberSurface.Data, this.ReturnNumberSurface.NoDataValue);
            }
            if (this.SourceIDSurface != null)
            {
                Array.Fill(this.SourceIDSurface.Data, this.SourceIDSurface.NoDataValue);
            }
        }

        public override void Reset(string filePath, Dataset rasterDataset, bool readData)
        {
            throw new NotImplementedException(); // TODO when needed
        }

        public override bool TryGetBand(string? name, [NotNullWhen(true)] out RasterBand? band)
        {
            if ((name == null) || (String.Equals(this.Surface.Name, name, StringComparison.Ordinal)))
            {
                band = this.Surface;
            }
            else if (String.Equals(this.CanopyMaxima3.Name, name, StringComparison.Ordinal))
            {
                band = this.CanopyMaxima3;
            }
            else if (String.Equals(this.CanopyHeight.Name, name, StringComparison.Ordinal))
            {
                band = this.CanopyHeight;
            }
            else if ((this.Subsurface != null) && String.Equals(this.Subsurface.Name, name, StringComparison.Ordinal))
            {
                band = this.Subsurface;
            }
            else if ((this.AerialMean != null) && String.Equals(this.AerialMean.Name, name, StringComparison.Ordinal))
            {
                band = this.AerialMean;
            }
            else if ((this.GroundMean != null) && String.Equals(this.GroundMean.Name, name, StringComparison.Ordinal))
            {
                band = this.GroundMean;
            }
            else if ((this.AerialPoints != null) && String.Equals(this.AerialPoints.Name, name, StringComparison.Ordinal))
            {
                band = this.AerialPoints;
            }
            else if ((this.GroundPoints != null) && String.Equals(this.GroundPoints.Name, name, StringComparison.Ordinal))
            {
                band = this.GroundPoints;
            }
            else if ((this.ReturnNumberSurface != null) && String.Equals(this.ReturnNumberSurface.Name, name, StringComparison.Ordinal))
            {
                band = this.ReturnNumberSurface;
            }
            else if ((this.SourceIDSurface != null) && String.Equals(this.SourceIDSurface.Name, name, StringComparison.Ordinal))
            {
                band = this.SourceIDSurface;
            }
            else
            {
                band = null;
                return false;
            }

            return true;
        }

        public override void Write(string dsmPath, bool compress)
        {
            // primary data bands
            // GDAL+GeoTIFF single type constraint: convert all bands to double and write with default no data value
            Debug.Assert(this.Surface.IsNoData(RasterBand.NoDataDefaultFloat) && this.CanopyMaxima3.IsNoData(RasterBand.NoDataDefaultFloat) && this.CanopyHeight.IsNoData(RasterBand.NoDataDefaultFloat));
            using Dataset dsmDataset = this.CreateGdalRasterAndSetFilePath(dsmPath, 3, DataType.GDT_Float32, compress);
            this.Surface.Write(dsmDataset, 1);
            this.CanopyMaxima3.Write(dsmDataset, 2);
            this.CanopyHeight.Write(dsmDataset, 3);
            this.FilePath = dsmPath;

            // diagnostic bands in z
            // Bands are single precision floating pointm sane as z values.
            int zDiagnosticBands = (this.Subsurface != null ? 1 : 0) + (this.AerialMean != null ? 1 : 0) + (this.GroundMean != null ? 1 : 0);
            if (zDiagnosticBands > 0)
            {
                string zTilePath = Raster.GetDiagnosticFilePath(dsmPath, DigitalSurfaceModel.DiagnosticDirectoryZ, createDiagnosticDirectory: true);

                using Dataset zDataset = this.CreateGdalRasterAndSetFilePath(zTilePath, zDiagnosticBands, DataType.GDT_Float32, compress);
                int gdalBand = 1;
                if (this.Subsurface != null)
                {
                    Debug.Assert(this.Subsurface.IsNoData(RasterBand.NoDataDefaultFloat));
                    this.Subsurface.Write(zDataset, gdalBand);
                    ++gdalBand;
                }
                if (this.AerialMean != null)
                {
                    Debug.Assert(this.AerialMean.IsNoData(RasterBand.NoDataDefaultFloat));
                    this.AerialMean.Write(zDataset, gdalBand);
                    ++gdalBand;
                }
                if (this.GroundMean != null)
                {
                    Debug.Assert(this.GroundMean.IsNoData(RasterBand.NoDataDefaultFloat));
                    this.GroundMean.Write(zDataset, gdalBand);
                }
            }
            else if ((this.Subsurface != null) || (this.AerialMean != null) || (this.GroundMean != null))
            {
                throw new NotSupportedException("Subsurface and mean elevation bands were not written as only one or two of the three layers is available.");
            }

            // diagnostic bands: point counts
            // Bands are unsigned 8, 16, or 32 bit integers depending on the largest value counted. 8 and 16 bit cases could potentially
            // be merged with source ID tiles but are not.
            if ((this.AerialPoints != null) && (this.GroundPoints != null))
            {
                Debug.Assert((this.AerialPoints.HasNoDataValue == false) && (this.GroundPoints.HasNoDataValue == false));
                string pointCountTilePath = Raster.GetDiagnosticFilePath(dsmPath, DigitalSurfaceModel.DiagnosticDirectoryPointCounts, createDiagnosticDirectory: true);
                DataType pointCountBandType = DataTypeExtensions.GetMostCompactIntegerType(this.AerialPoints, this.GroundPoints);

                using Dataset pointCountDataset = this.CreateGdalRasterAndSetFilePath(pointCountTilePath, 2, pointCountBandType, compress);
                this.AerialPoints.Write(pointCountDataset, 1);
                this.GroundPoints.Write(pointCountDataset, 2);
                Debug.Assert(pointCountDataset.RasterCount == 2);
            }
            else if ((this.AerialPoints != null) || (this.GroundPoints != null))
            {
                throw new NotSupportedException("Point count bands were not written as only one of the two layers is available.");
            }

            // diagnostic bands: return number
            // For now, write band even if return numbers aren't defined in the data source.
            if (this.ReturnNumberSurface != null)
            {
                string returnNumberTilePath = Raster.GetDiagnosticFilePath(dsmPath, DigitalSurfaceModel.DiagnosticDirectoryReturnNumber, createDiagnosticDirectory: true);

                using Dataset returnNumberDataset = this.CreateGdalRasterAndSetFilePath(returnNumberTilePath, 1, DataType.GDT_Byte, compress);
                this.ReturnNumberSurface.Write(returnNumberDataset, 1);
                Debug.Assert(returnNumberDataset.RasterCount == 1);
            }

            // diagnostic bands: source IDs
            // For now, write band even if the maximum source ID is zero.
            if (this.SourceIDSurface != null)
            {
                string sourceIDtilePath = Raster.GetDiagnosticFilePath(dsmPath, DigitalSurfaceModel.DiagnosticDirectorySourceID, createDiagnosticDirectory: true);
                DataType sourceIDbandType = DataTypeExtensions.GetMostCompactIntegerType(this.SourceIDSurface); // could cache this when points are being added

                using Dataset sourceIDdataset = this.CreateGdalRasterAndSetFilePath(sourceIDtilePath, 1, sourceIDbandType, compress);
                this.SourceIDSurface.Write(sourceIDdataset, 1);
                Debug.Assert(sourceIDdataset.RasterCount == 1);
            }
        }
    }
}