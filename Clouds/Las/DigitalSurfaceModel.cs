using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Mars.Clouds.Las
{
    public class DigitalSurfaceModel : Raster, IRasterSerializable<DigitalSurfaceModel>
    {
        private const string DiagnosticDirectoryPointCounts = "nPoints";
        private const string DiagnosticDirectorySourceID = "sourceID";
        private const string DiagnosticDirectoryZ = "z";

        public const string AerialPointsBandName = "nAerial";
        public const string CanopyHeightBandName = "chm";
        public const string CanopyMaximaBandName = "cmm3";
        public const string SurfaceBandName = "dsm";
        public const string AerialMeanBandName = "aerialMean";
        public const string GroundMeanBandName = "groundMean";
        public const string GroundPointsBandName = "nGround";
        public const string SourceIDSurfaceBandName = "sourceIDsurface";

        // primary data bands
        public RasterBand<float> Surface { get; private set; } // digital surface model
        public RasterBand<float> CanopyMaxima3 { get; private set; } // canopy maxima model obtained from the digital surface model using a 3x3 kernel
        public RasterBand<float> CanopyHeight { get; private set; } // canopy height model obtained from DSM - DTM

        // diagnostic bands in z
        // Digital terrain model can be calculated as DTM = DSM - CHM or stored separately.
        public RasterBand<float>? AerialMean { get; private set; } // mean elevation of aerial points in cell
        public RasterBand<float>? GroundMean { get; private set; } // mean elevation of ground points in cell

        // diagnostic bands: point counts
        public RasterBand<UInt32>? AerialPoints { get; private set; } // number of points in cell not classified as ground
        public RasterBand<UInt32>? GroundPoints { get; private set; } // number of ground points in cell

        // diagnostic bands: source IDs
        public RasterBand<UInt16>? SourceIDSurface { get; private set; }

        public DigitalSurfaceModel(string dsmFilePath, LasFile lasFile, RasterBand<float> dtmTile)
            : base(lasFile.GetSpatialReference(), dtmTile.Transform, dtmTile.SizeX, dtmTile.SizeY)
        {
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
            this.AerialMean = new(this, DigitalSurfaceModel.AerialMeanBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData);
            this.GroundMean = new(this, DigitalSurfaceModel.GroundMeanBandName, RasterBand.NoDataDefaultFloat, RasterBandInitialValue.NoData);
            this.AerialPoints = new(this, DigitalSurfaceModel.AerialPointsBandName, RasterBandInitialValue.Default); // leave at default of zero, lacks no data value as count of zero is valid
            this.GroundPoints = new(this, DigitalSurfaceModel.GroundPointsBandName, RasterBandInitialValue.Default); // leave at default of zero, lacks no data value as count of zero is valid
            this.SourceIDSurface = new(this, DigitalSurfaceModel.SourceIDSurfaceBandName, 0, RasterBandInitialValue.NoData); // set no data to zero and leave at default of zero as LAS spec defines source IDs 1-65535 as valid
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
            if (this.AerialMean != null)
            {
                if (String.Equals(name, this.AerialMean.Name, StringComparison.Ordinal))
                {
                    return 3;
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

            if (this.SourceIDSurface != null)
            {
                if (String.Equals(name, this.SourceIDSurface.Name, StringComparison.Ordinal))
                {
                    return bandIndex;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(name), "No band named '" + name + "' found in raster.");
        }

        // TODO: SIMD
        public void OnPointAdditionComplete(RasterBand<float> dtm)
        {
            if (SpatialReferenceExtensions.IsSameCrs(this.Crs, dtm.Crs) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(dtm), "DSMs and DTMs are currently required to be in the same CRS. The DSM CRS is '" + this.Crs.GetName() + "' while the DTM CRS is " + dtm.Crs.GetName() + ".");
            }
            if (this.IsSameExtentAndSpatialResolution(dtm) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(dtm), "DTM extent (" + dtm.GetExtentString() + ") or size (" + dtm.SizeX + " x " + dtm.SizeY + ") does not match DSM extent (" + this.GetExtentString() + ") or size (" + this.SizeX + " by " + this.SizeY + ").");
            }

            // canopy height is calculated relative to DTM
            // If needed, mean ground elevation can be used instead.
            for (int cellIndex = 0; cellIndex < this.Cells; ++cellIndex)
            {
                this.CanopyHeight[cellIndex] = this.Surface[cellIndex] - dtm[cellIndex];
            }

            // find mean elevations of aerial and ground points
            if (this.AerialPoints != null)
            {
                Debug.Assert(this.AerialMean != null);
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
            if (this.GroundPoints != null)
            {
                Debug.Assert(this.GroundMean != null);
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

            // for now, population of the canopy maxima model is deferred until a virtual raster neighborhood is available
            // Binomial.Smooth3x3(this.Surface, this.CanopyMaxima3);
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
            // Bands are single precision floating point as that is the minimum supported size.
            if ((this.AerialMean != null) && (this.GroundMean != null))
            {
                Debug.Assert(this.AerialMean.IsNoData(RasterBand.NoDataDefaultFloat) && this.GroundMean.IsNoData(RasterBand.NoDataDefaultFloat));
                string zTilePath = Raster.GetDiagnosticFilePath(dsmPath, DigitalSurfaceModel.DiagnosticDirectoryZ, createDiagnosticDirectory: true);

                using Dataset zDataset = this.CreateGdalRasterAndSetFilePath(zTilePath, 2, DataType.GDT_Float32, compress);
                this.AerialMean.Write(zDataset, 1);
                this.GroundMean.Write(zDataset, 2);
                Debug.Assert(zDataset.RasterCount == 2);
            }
            else if ((this.AerialMean != null) || (this.GroundMean != null))
            {
                throw new NotSupportedException("Mean elevation bands were not written as only one of the two layers is available.");
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
            
            // diagnostic bands: source IDs
            // For now, write bands even if the maximum source ID is zero.
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