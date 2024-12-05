using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using MaxRev.Gdal.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Diagnostics;
using System.IO;

namespace Mars.Clouds.UnitTests
{
    [TestClass]
    public class LasTests : CloudTest
    {
        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext _)
        {
            GdalBase.ConfigureAll();
        }

        private VirtualRaster<Raster<float>> ReadDtm()
        {
            Debug.Assert(this.UnitTestPath != null);

            VirtualRaster<Raster<float>> dtm = new();
            Raster<float> tile = Raster<float>.CreateFromBandMetadata(Path.Combine(this.UnitTestPath, TestConstant.DtmFileName));
            tile.ReadBandData();
            dtm.Add(tile);
            dtm.CreateTileGrid();

            return dtm;
        }

        [TestMethod]
        public void ReadLasToDsm()
        {
            LasTile lasTile = this.ReadLasTile();
            (double lasTileCentroidX, double lasTileCentroidY) = lasTile.GridExtent.GetCentroid();
            VirtualRaster<Raster<float>> dtmRaster = this.ReadDtm();
            Assert.IsTrue((dtmRaster.NonNullTileCount == 1) && (dtmRaster.Crs.IsVertical() == 0));
            Assert.IsTrue((dtmRaster.Crs.IsSameGeogCS(lasTile.GetSpatialReference()) == 1) && SpatialReferenceExtensions.IsSameCrs(dtmRaster.Crs, lasTile.GetSpatialReference()));
            Assert.IsTrue(dtmRaster.TryGetTileBand(lasTileCentroidX, lasTileCentroidY, bandName: null, out RasterBand<float>? dtmTile)); // no band name set in DTM

            RasterBandPool dataBufferPool = new();
            DigitalSurfaceModel dsmTile = new("dsmRaster ReadLasToDsm.tif", lasTile, DigitalSurfaceModelBands.Default | DigitalSurfaceModelBands.Subsurface | DigitalSurfaceModelBands.ReturnNumberSurface, dtmTile, dataBufferPool);

            using LasReader pointReader = lasTile.CreatePointReader(unbuffered: false, enableAsync: false);
            byte[]? pointReadBuffer = null;
            float[]? subsurfaceBuffer = null;
            pointReader.ReadPointsToDsm(lasTile, dsmTile, ref pointReadBuffer, ref subsurfaceBuffer);
            dsmTile.OnPointAdditionComplete(dtmTile, 1.0F, subsurfaceBuffer);
            
            VirtualRaster<DigitalSurfaceModel> dsmVirtualRaster = new();
            dsmVirtualRaster.Add(dsmTile);
            dsmVirtualRaster.CreateTileGrid();
            RasterNeighborhood8<float> dsmNeighborhood = dsmVirtualRaster.GetNeighborhood8<float>(tileGridIndexX: 0, tileGridIndexY: 0, bandName: "dsm");
            Binomial.Smooth3x3(dsmNeighborhood, dsmTile.CanopyMaxima3);

            Assert.IsTrue((dsmTile.AerialPoints != null) && (dsmTile.Subsurface != null) && (dsmTile.AerialMean != null) && (dsmTile.GroundMean != null) && (dsmTile.GroundPoints != null) && (dsmTile.ReturnNumberSurface != null) && (dsmTile.SourceIDSurface != null));
            Assert.IsTrue((dsmTile.SizeX == dtmRaster.TileSizeInCellsX) && (dsmTile.SizeY == dtmRaster.TileSizeInCellsY));

            for (int cellIndex = 0; cellIndex < dsmTile.Cells; ++cellIndex)
            {
                UInt32 aerialPoints = dsmTile.AerialPoints[cellIndex];
                UInt32 groundPoints = dsmTile.GroundPoints[cellIndex];
                Assert.IsTrue(aerialPoints < 50000);
                Assert.IsTrue(groundPoints == 0); // no points classified as ground

                float dsmZ = dsmTile.Surface[cellIndex];
                float cmm3 = dsmTile.CanopyMaxima3[cellIndex];
                float chmZ = dsmTile.CanopyHeight[cellIndex];
                float subsurface = dsmTile.Subsurface[cellIndex];
                float meanZ = dsmTile.AerialMean[cellIndex];

                float dtmZ = dtmTile[cellIndex];
                if (aerialPoints > 0)
                {
                    Assert.IsTrue((dsmZ > 920.0F) && (dsmZ < 930.0F));
                    Assert.IsTrue((cmm3 > 920.0F) && (cmm3 < 930.0F)); // if DSM is relatively low, CMM3 can be higher
                    Assert.IsTrue(chmZ == dsmZ - dtmZ);
                    Assert.IsTrue((dsmZ > meanZ) && (meanZ > dtmZ - 0.2F)); // 20 cm allowance for alignment margin, point error, and DTM error
                    Assert.IsTrue((subsurface > dtmZ - 0.5F) && (subsurface < dsmZ));
                }
                else
                {
                    Assert.IsTrue(Single.IsNaN(dsmZ));
                    Assert.IsTrue(Single.IsNaN(cmm3));
                    Assert.IsTrue(Single.IsNaN(chmZ));
                    Assert.IsTrue(Single.IsNaN(subsurface));
                    Assert.IsTrue(Single.IsNaN(meanZ));
                }

                Assert.IsTrue(Single.IsNaN(dtmZ) || (dtmTile.Data[cellIndex] == dtmZ)); // DTM contains -9999 no datas which are converted to DSM no datas
                Assert.IsTrue(Single.IsNaN(dsmTile.GroundMean[cellIndex])); // no points classified as ground
                Assert.IsTrue(dsmTile.ReturnNumberSurface[cellIndex] == 0); // return numbers not set in point cloud
                Assert.IsTrue(dsmTile.SourceIDSurface[cellIndex] == 0); // point source ID not set in point cloud
            }
        }

        [TestMethod]
        public void ReadLasToGridMetrics()
        {
            Debug.Assert(this.UnitTestPath != null);

            FileInfo lasFileInfo = new(Path.Combine(this.UnitTestPath, TestConstant.LasFileName));
            using FileStream stream = new(lasFileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, LasReader.HeaderAndVlrReadBufferSizeInBytes);
            using LasReader headerVlrReader = new(stream);
            LasTile lasTile = new(lasFileInfo.FullName, headerVlrReader, fallbackCreationDate: null);

            VirtualRaster<Raster<float>> dtm = this.ReadDtm();

            LasHeader14 lasHeader14 = (LasHeader14)lasTile.Header;
            Assert.IsTrue(String.Equals(lasHeader14.FileSignature, LasFile.Signature, StringComparison.Ordinal));
            Assert.IsTrue(lasHeader14.GlobalEncoding == GlobalEncoding.WellKnownText);
            Assert.IsTrue(lasHeader14.FileSourceID == 0);
            Assert.IsTrue(lasHeader14.ProjectID == Guid.Empty);
            Assert.IsTrue(lasHeader14.VersionMajor == 1);
            Assert.IsTrue(lasHeader14.VersionMinor == 4);
            Assert.IsTrue(String.IsNullOrWhiteSpace(lasHeader14.SystemIdentifier));
            Assert.IsTrue(String.Equals(lasHeader14.GeneratingSoftware, "QT Modeler 8.4.1836", StringComparison.Ordinal));
            Assert.IsTrue(lasHeader14.FileCreationDayOfYear == 282);
            Assert.IsTrue(lasHeader14.FileCreationYear == 2023);
            Assert.IsTrue(lasHeader14.HeaderSize == LasHeader14.HeaderSizeInBytes);
            Assert.IsTrue(lasHeader14.OffsetToPointData == 1937);
            Assert.IsTrue(lasHeader14.NumberOfVariableLengthRecords == 1);
            Assert.IsTrue(lasHeader14.PointDataRecordFormat == 6);
            Assert.IsTrue(lasHeader14.PointDataRecordLength == 30);
            Assert.IsTrue(lasHeader14.LegacyNumberOfPointRecords == 207617);
            Assert.IsTrue(lasHeader14.LegacyNumberOfPointsByReturn[0] == 0);
            Assert.IsTrue(lasHeader14.LegacyNumberOfPointsByReturn[1] == 0);
            Assert.IsTrue(lasHeader14.LegacyNumberOfPointsByReturn[2] == 0);
            Assert.IsTrue(lasHeader14.LegacyNumberOfPointsByReturn[3] == 0);
            Assert.IsTrue(lasHeader14.LegacyNumberOfPointsByReturn[4] == 0);
            Assert.IsTrue(lasHeader14.XScaleFactor == 0.0001);
            Assert.IsTrue(lasHeader14.YScaleFactor == 0.0001);
            Assert.IsTrue(lasHeader14.ZScaleFactor == 0.0001);
            Assert.IsTrue(lasHeader14.XOffset == 608571.86565364827);
            Assert.IsTrue(lasHeader14.YOffset == 4926891.3199104257);
            Assert.IsTrue(lasHeader14.ZOffset == 912.835012265612);
            Assert.IsTrue(lasHeader14.MaxX == 608760.87655364827);
            Assert.IsTrue(lasHeader14.MinX == 608757.26795364823);
            Assert.IsTrue(lasHeader14.MaxY == 4927011.2347104261);
            Assert.IsTrue(lasHeader14.MinY == 4927007.0106104258);
            Assert.IsTrue(lasHeader14.MaxZ == 928.63061226561206);
            Assert.IsTrue(lasHeader14.MinZ == 920.520912265612);
            Assert.IsTrue(lasHeader14.StartOfWaveformDataPacketRecord == 0);
            Assert.IsTrue(lasHeader14.StartOfFirstExtendedVariableLengthRecord == (UInt64)lasFileInfo.Length); // not well defined by spec; proactively setting to end of file is reasonable when there are zero EVLRs
            Assert.IsTrue(lasHeader14.NumberOfExtendedVariableLengthRecords == 0);
            Assert.IsTrue(lasHeader14.NumberOfPointRecords == 207617);
            Assert.IsTrue(lasHeader14.NumberOfPointsByReturn[0] == 0);
            Assert.IsTrue(lasHeader14.NumberOfPointsByReturn[1] == 0);
            Assert.IsTrue(lasHeader14.NumberOfPointsByReturn[2] == 0);
            Assert.IsTrue(lasHeader14.NumberOfPointsByReturn[3] == 0);
            Assert.IsTrue(lasHeader14.NumberOfPointsByReturn[4] == 0);
            Assert.IsTrue(lasHeader14.NumberOfPointsByReturn[5] == 0);
            Assert.IsTrue(lasHeader14.NumberOfPointsByReturn[6] == 0);
            Assert.IsTrue(lasHeader14.NumberOfPointsByReturn[7] == 0);
            Assert.IsTrue(lasHeader14.NumberOfPointsByReturn[8] == 0);
            Assert.IsTrue(lasHeader14.NumberOfPointsByReturn[9] == 0);
            Assert.IsTrue(lasHeader14.NumberOfPointsByReturn[10] == 0);
            Assert.IsTrue(lasHeader14.NumberOfPointsByReturn[11] == 0);
            Assert.IsTrue(lasHeader14.NumberOfPointsByReturn[12] == 0);
            Assert.IsTrue(lasHeader14.NumberOfPointsByReturn[13] == 0);
            Assert.IsTrue(lasHeader14.NumberOfPointsByReturn[14] == 0);
            Assert.IsTrue(lasHeader14.GetNumberOfPoints() == 207617);
            Assert.IsTrue(lasTile.GridExtent.XMin == lasHeader14.MinX);
            Assert.IsTrue(lasTile.GridExtent.XMax == lasHeader14.MaxX);
            Assert.IsTrue(lasTile.GridExtent.YMin == lasHeader14.MinY);
            Assert.IsTrue(lasTile.GridExtent.YMax == lasHeader14.MaxY);

            Assert.IsTrue(lasTile.VariableLengthRecords.Count == 1);
            OgcCoordinateSystemWktRecord crs = (OgcCoordinateSystemWktRecord)lasTile.VariableLengthRecords[0];
            Assert.IsTrue(crs.Reserved == 0); // bug in originating writer; should be zero
            Assert.IsTrue(String.Equals(crs.UserID, "LASF_Projection", StringComparison.Ordinal));
            Assert.IsTrue(crs.RecordID == OgcCoordinateSystemWktRecord.LasfProjectionRecordID);
            Assert.IsTrue(crs.RecordLengthAfterHeader == 854);
            Assert.IsTrue(String.Equals(crs.Description, "WKT Projection", StringComparison.Ordinal));
            string wkt = crs.SpatialReference.GetWkt();
            Assert.IsTrue(String.Equals(wkt, "COMPD_CS[\"WGS 84 / UTM zone 10N + NAVD88 height\",PROJCS[\"WGS 84 / UTM zone 10N\",GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563,AUTHORITY[\"EPSG\",\"7030\"]],AUTHORITY[\"EPSG\",\"6326\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"4326\"]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",-123],PARAMETER[\"scale_factor\",0.9996],PARAMETER[\"false_easting\",500000],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH],AUTHORITY[\"EPSG\",\"32610\"]],VERT_CS[\"NAVD88 height\",VERT_DATUM[\"North American Vertical Datum 1988\",2005,AUTHORITY[\"EPSG\",\"5103\"]],UNIT[\"metre\",1,AUTHORITY[\"EPSG\",\"9001\"]],AXIS[\"Gravity-related height\",UP],AUTHORITY[\"EPSG\",\"5703\"]]]", StringComparison.Ordinal));

            Assert.IsTrue(lasTile.ExtendedVariableLengthRecords.Count == 0);

            int lasFileEpsg = lasTile.GetProjectedCoordinateSystemEpsg();
            Assert.IsTrue(lasFileEpsg == 32610);

            Raster<UInt16> gridCellDefinitions = this.SnapPointCloudTileToGridCells(lasTile);
            GridGeoTransform lasFileTransform = new(lasTile.GridExtent, gridCellDefinitions.Transform.CellWidth, gridCellDefinitions.Transform.CellHeight);
            LasTileGrid lasGrid = new(lasTile.GetSpatialReference(), lasFileTransform, 1, 1, [ lasTile ]);
            int tileGridSizeX = (int)(lasTile.GridExtent.Width / gridCellDefinitions.Transform.CellWidth) + 1;
            int tileGridSizeY = (int)(-lasTile.GridExtent.Height / gridCellDefinitions.Transform.CellHeight) + 1;
            GridMetricsPointLists tilePoints = new(lasGrid, lasTile, gridCellDefinitions.Transform.CellWidth, tileGridSizeX, tileGridSizeY, gridCellDefinitions.GetBand(name: null));

            Assert.IsTrue(SpatialReferenceExtensions.IsSameCrs(gridCellDefinitions.Crs, tilePoints.Crs));
            Assert.IsTrue(tilePoints.SizeX == tileGridSizeX);
            Assert.IsTrue(tilePoints.SizeY == tileGridSizeY);
            Assert.IsTrue(Int32.Parse(tilePoints.Crs.GetAuthorityCode("PROJCS")) == 32610);
            int populatedGridCells = 0;
            for (int yIndex = 0; yIndex < tilePoints.SizeY; ++yIndex)
            {
                for (int xIndex = 0; xIndex < tilePoints.SizeX; ++xIndex)
                {
                    if (tilePoints[xIndex, yIndex] != null)
                    {
                        ++populatedGridCells;
                    }
                }
            }
            Assert.IsTrue(populatedGridCells == 4); // two cells with points (8, 14 and 9, 14) plus two cells without points due to proxied tile size (8, 15 and 9, 15)

            byte[]? pointReadBuffer = null;
            using LasReader pointReader = lasTile.CreatePointReader();
            pointReader.ReadPointsToGrid(lasTile, tilePoints, ref pointReadBuffer);
            PointListZirnc? psmeCell1 = tilePoints[0, 0];
            Assert.IsTrue(psmeCell1 != null);
            Assert.IsTrue(psmeCell1.TilesIntersected == 1);
            Assert.IsTrue(psmeCell1.TilesLoaded == 1);
            Assert.IsTrue(psmeCell1.Count == 162764);
            Assert.IsTrue(psmeCell1.Z.Count == psmeCell1.Count);
            Assert.IsTrue(psmeCell1.Intensity.Count == psmeCell1.Count);
            Assert.IsTrue(psmeCell1.ReturnNumber.Count == psmeCell1.Count);
            Assert.IsTrue(psmeCell1.Classification.Count == psmeCell1.Count);

            PointListZirnc? psmeCell2 = tilePoints[1, 0];
            Assert.IsTrue(psmeCell2 != null);
            Assert.IsTrue(psmeCell2.TilesIntersected == 1);
            Assert.IsTrue(psmeCell2.TilesLoaded == 1);
            Assert.IsTrue(psmeCell2.Count == 44853);
            Assert.IsTrue(psmeCell2.Z.Count == psmeCell2.Count);
            Assert.IsTrue(psmeCell2.Intensity.Count == psmeCell2.Count);
            Assert.IsTrue(psmeCell2.ReturnNumber.Count == psmeCell2.Count);
            Assert.IsTrue(psmeCell2.Classification.Count == psmeCell2.Count);

            PointListZirnc? adjacentCell1 = tilePoints[0, 1];
            Assert.IsTrue(adjacentCell1 != null);
            Assert.IsTrue(adjacentCell1.TilesIntersected == 1);
            Assert.IsTrue(adjacentCell1.TilesLoaded == 1);
            Assert.IsTrue(adjacentCell1.Count == 0);

            PointListZirnc? adjacentCell2 = tilePoints[1, 1];
            Assert.IsTrue(adjacentCell2 != null);
            Assert.IsTrue(adjacentCell2.TilesIntersected == 1);
            Assert.IsTrue(adjacentCell2.TilesLoaded == 1);
            Assert.IsTrue(adjacentCell2.Count == 0);

            GridMetricsSettings metricsSettings = new()
            {
                IntensityPCumulativeZQ = true,
                IntensityPGround = true,
                IntensityTotal = true,
                Kurtosis = true,
                ZPCumulative = true,
                ZQFives = true
            };
            SpatialReference lasSpatialReference = lasTile.GetSpatialReference();
            float crsLinearUnits = (float)lasSpatialReference.GetLinearUnits();
            float oneMeterHeightClass = 1.0F / crsLinearUnits;
            float twoMeterHeightThreshold = 2.0F / crsLinearUnits;

            GridMetricsRaster metricsRasterMonolithic = new(gridCellDefinitions, metricsSettings);

            (double psmeCell1X, double psmeCell1Y) = metricsRasterMonolithic.Transform.GetCellCenter(8, 14);
            (int psmeCell1metricsIndexX, int psmeCell1metricsIndexY) = metricsRasterMonolithic.ToGridIndices(psmeCell1X, psmeCell1Y);
            Assert.IsTrue(dtm.TryGetNeighborhood8(psmeCell1X, psmeCell1Y, bandName: null, out RasterNeighborhood8<float>? psmeDtmNeighborhood1));
            (double psmeCell2X, double psmeCell2Y) = metricsRasterMonolithic.Transform.GetCellCenter(9, 14);
            (int psmeCell2metricsIndexX, int psmeCell2metricsIndexY) = metricsRasterMonolithic.ToGridIndices(psmeCell2X, psmeCell2Y);
            Assert.IsTrue(dtm.TryGetNeighborhood8(psmeCell2X, psmeCell2Y, bandName: null, out RasterNeighborhood8<float>? psmeDtmNeighborhood2));
            (double adjacentCell1X, double adjacentCell1Y) = metricsRasterMonolithic.Transform.GetCellCenter(8, 15);
            (int adjacentCell1metricsIndexX, int adjacentCell1metricsIndexY) = metricsRasterMonolithic.ToGridIndices(adjacentCell1X, adjacentCell1Y);
            Assert.IsTrue(dtm.TryGetNeighborhood8(adjacentCell1X, adjacentCell1Y, bandName: null, out RasterNeighborhood8<float>? adjacentCellNeighborhood1));
            (double adjacentCell2X, double adjacentCell2Y) = metricsRasterMonolithic.Transform.GetCellCenter(9, 15);
            (int adjacentCell2metricsIndexX, int adjacentCell2metricsIndexY) = metricsRasterMonolithic.ToGridIndices(adjacentCell2X, adjacentCell2Y);
            Assert.IsTrue(dtm.TryGetNeighborhood8(adjacentCell2X, adjacentCell2Y, bandName: null, out RasterNeighborhood8<float>? adjacentCellNeighborhood2));

            float[]? sortedZ = null;
            UInt16[]? sortedIntensity = null;
            metricsRasterMonolithic.SetMetrics(psmeCell1metricsIndexX, psmeCell1metricsIndexY, psmeCell1, psmeDtmNeighborhood1, oneMeterHeightClass, twoMeterHeightThreshold, ref sortedZ, ref sortedIntensity);
            metricsRasterMonolithic.SetMetrics(psmeCell2metricsIndexX, psmeCell2metricsIndexY, psmeCell2, psmeDtmNeighborhood2, oneMeterHeightClass, twoMeterHeightThreshold, ref sortedZ, ref sortedIntensity);
            metricsRasterMonolithic.SetMetrics(adjacentCell1metricsIndexX, adjacentCell1metricsIndexY, adjacentCell1, adjacentCellNeighborhood1, oneMeterHeightClass, twoMeterHeightThreshold, ref sortedZ, ref sortedIntensity);
            metricsRasterMonolithic.SetMetrics(adjacentCell2metricsIndexX, adjacentCell2metricsIndexY, adjacentCell2, adjacentCellNeighborhood1, oneMeterHeightClass, twoMeterHeightThreshold, ref sortedZ, ref sortedIntensity);

            Assert.IsTrue((metricsRasterMonolithic.ZQuantile05 != null) && (metricsRasterMonolithic.ZQuantile15 != null) && (metricsRasterMonolithic.ZQuantile25 != null) && (metricsRasterMonolithic.ZQuantile35 != null) && (metricsRasterMonolithic.ZQuantile45 != null) && (metricsRasterMonolithic.ZQuantile55 != null) && (metricsRasterMonolithic.ZQuantile65 != null) && (metricsRasterMonolithic.ZQuantile75 != null) && (metricsRasterMonolithic.ZQuantile85 != null) && (metricsRasterMonolithic.ZQuantile95 != null));
            Assert.IsTrue((metricsRasterMonolithic.IntensityKurtosis != null) && (metricsRasterMonolithic.ZKurtosis != null));
            Assert.IsTrue((metricsRasterMonolithic.IntensityPCumulativeZQ10 != null) && (metricsRasterMonolithic.IntensityPCumulativeZQ30 != null) && (metricsRasterMonolithic.IntensityPCumulativeZQ50 != null) && (metricsRasterMonolithic.IntensityPCumulativeZQ70 != null) && (metricsRasterMonolithic.IntensityPCumulativeZQ90 != null) && (metricsRasterMonolithic.IntensityPGround != null));
            Assert.IsTrue(metricsRasterMonolithic.IntensityTotal != null);
            Assert.IsTrue((metricsRasterMonolithic.ZPCumulative10 != null) && (metricsRasterMonolithic.ZPCumulative20 != null) && (metricsRasterMonolithic.ZPCumulative30 != null) && (metricsRasterMonolithic.ZPCumulative40 != null) && (metricsRasterMonolithic.ZPCumulative50 != null) && (metricsRasterMonolithic.ZPCumulative60 != null) && (metricsRasterMonolithic.ZPCumulative70 != null) && (metricsRasterMonolithic.ZPCumulative80 != null) && (metricsRasterMonolithic.ZPCumulative90 != null));

            Assert.IsTrue(metricsRasterMonolithic.AcceptedPoints[8, 14] == 162764);
            Assert.IsTrue(metricsRasterMonolithic.ZMax[8, 14] == 928.6306F);
            Assert.IsTrue(metricsRasterMonolithic.ZMean[8, 14] == 923.698242F);
            Assert.IsTrue(metricsRasterMonolithic.ZGroundMean[8, 14] == 920.8271F);
            Assert.IsTrue(metricsRasterMonolithic.ZStandardDeviation[8, 14] == 1.69425166F);
            Assert.IsTrue(metricsRasterMonolithic.ZSkew[8, 14] == 0.3093502F);
            Assert.IsTrue(metricsRasterMonolithic.ZKurtosis[8, 14] == 2.619736F);
            Assert.IsTrue(metricsRasterMonolithic.ZNormalizedEntropy[8, 14] == 0.83192575F);
            Assert.IsTrue(metricsRasterMonolithic.PZAboveZMean[8, 14] == 0.4679106F);
            Assert.IsTrue(metricsRasterMonolithic.PZAboveThreshold[8, 14] == 0.666455746F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile05[8, 14] == 920.903137F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile10[8, 14] == 921.4857F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile15[8, 14] == 921.985352F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile20[8, 14] == 922.275F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile25[8, 14] == 922.4862F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile30[8, 14] == 922.6903F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile35[8, 14] == 922.8859F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile40[8, 14] == 923.052551F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile45[8, 14] == 923.2003F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile50[8, 14] == 923.4795F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile55[8, 14] == 923.8576F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile60[8, 14] == 924.2154F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile65[8, 14] == 924.2943F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile70[8, 14] == 924.5219F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile75[8, 14] == 924.852234F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile80[8, 14] == 925.0855F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile85[8, 14] == 925.5673F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile90[8, 14] == 925.9668F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile95[8, 14] == 926.7014F);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative10[8, 14] == 0.08913519F);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative20[8, 14] == 0.181962848F);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative30[8, 14] == 0.3759185F);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative40[8, 14] == 0.5440515F);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative50[8, 14] == 0.708209455F);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative60[8, 14] == 0.8307365F);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative70[8, 14] == 0.911983F);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative80[8, 14] == 0.958602667F);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative90[8, 14] == 0.9956993F);
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityFirstReturn[8, 14]));
            Assert.IsTrue(metricsRasterMonolithic.IntensityMean[8, 14] == 5679.68066F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityMeanAboveMedianZ[8, 14] == 6255.316F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityMeanBelowMedianZ[8, 14] == 5104.03027F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityMax[8, 14] == 29812.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityStandardDeviation[8, 14] == 3675.354F);
            Assert.IsTrue(metricsRasterMonolithic.IntensitySkew[8, 14] == 0.610383332F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityKurtosis[8, 14] == 2.95583248F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityTotal[8, 14] == 9.244475E+08F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityQuantile10[8, 14] == 1285.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityQuantile20[8, 14] == 2313.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityQuantile30[8, 14] == 3084.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityQuantile40[8, 14] == 4112.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityQuantile50[8, 14] == 5140.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityQuantile60[8, 14] == 6425.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityQuantile70[8, 14] == 7453.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityQuantile80[8, 14] == 8995.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityQuantile90[8, 14] == 10794.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityPCumulativeZQ10[8, 14] == 0.102303207F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityPCumulativeZQ30[8, 14] == 0.2589203F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityPCumulativeZQ50[8, 14] == 0.44931823F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityPCumulativeZQ70[8, 14] == 0.6531651F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityPCumulativeZQ90[8, 14] == 0.8864496F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityPGround[8, 14] == 0.0F); // points not classified
            Assert.IsTrue(metricsRasterMonolithic.PFirstReturn[8, 14] == 0.0F); // defect in manufacturer proprietary software generating point cloud: no return numbers set
            Assert.IsTrue(metricsRasterMonolithic.PSecondReturn[8, 14] == 0.0F);
            Assert.IsTrue(metricsRasterMonolithic.PThirdReturn[8, 14] == 0.0F);
            Assert.IsTrue(metricsRasterMonolithic.PFourthReturn[8, 14] == 0.0F);
            Assert.IsTrue(metricsRasterMonolithic.PFifthReturn[8, 14] == 0.0F);
            Assert.IsTrue(metricsRasterMonolithic.PGround[8, 14] == 0.0F); // points not classified

            Assert.IsTrue(metricsRasterMonolithic.AcceptedPoints[9, 14] == 44853.0F);
            Assert.IsTrue(metricsRasterMonolithic.ZMax[9, 14] == 926.576538F);
            Assert.IsTrue(metricsRasterMonolithic.ZMean[9, 14] == 922.74585F);
            Assert.IsTrue(metricsRasterMonolithic.ZGroundMean[9, 14] == 921.0524F);
            Assert.IsTrue(metricsRasterMonolithic.ZStandardDeviation[9, 14] == 1.39960063F);
            Assert.IsTrue(metricsRasterMonolithic.ZSkew[9, 14] == 0.329364121F);
            Assert.IsTrue(metricsRasterMonolithic.ZKurtosis[9, 14] == 2.12710214F);
            Assert.IsTrue(metricsRasterMonolithic.ZNormalizedEntropy[9, 14] == 0.828225255F);
            Assert.IsTrue(metricsRasterMonolithic.PZAboveZMean[9, 14] == 0.454975128F);
            Assert.IsTrue(metricsRasterMonolithic.PZAboveThreshold[9, 14] == 0.413171917F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile05[9, 14] == 920.749F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile10[9, 14] == 920.872437F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile15[9, 14] == 921.17804F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile20[9, 14] == 921.4686F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile25[9, 14] == 921.5838F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile30[9, 14] == 921.74884F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile35[9, 14] == 921.9464F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile40[9, 14] == 922.1297F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile45[9, 14] == 922.2642F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile50[9, 14] == 922.537537F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile55[9, 14] == 922.775452F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile60[9, 14] == 923.1283F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile65[9, 14] == 923.2835F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile70[9, 14] == 923.620544F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile75[9, 14] == 923.9276F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile80[9, 14] == 924.252F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile85[9, 14] == 924.3579F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile90[9, 14] == 924.6782F);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile95[9, 14] == 925.027832F);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative10[9, 14] == 0.140503421F);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative20[9, 14] == 0.295008123F);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative30[9, 14] == 0.4641607F);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative40[9, 14] == 0.569215F);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative50[9, 14] == 0.688471258F);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative60[9, 14] == 0.7677524F);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative70[9, 14] == 0.907653868F);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative80[9, 14] == 0.972644F);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative90[9, 14] == 0.993043959F);
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityFirstReturn[9, 14]));
            Assert.IsTrue(metricsRasterMonolithic.IntensityMean[9, 14] == 4962.5127F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityMeanAboveMedianZ[9, 14] == 5286.88F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityMeanBelowMedianZ[9, 14] == 4638.102F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityMax[9, 14] == 27242.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityStandardDeviation[9, 14] == 3340.16382F);
            Assert.IsTrue(metricsRasterMonolithic.IntensitySkew[9, 14] == 0.6552501F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityKurtosis[9, 14] == 2.89136815F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityTotal[9, 14] == 222583584.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityQuantile10[9, 14] == 771.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityQuantile20[9, 14] == 2056.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityQuantile30[9, 14] == 2827.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityQuantile40[9, 14] == 3341.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityQuantile50[9, 14] == 4369.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityQuantile60[9, 14] == 5397.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityQuantile70[9, 14] == 6682.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityQuantile80[9, 14] == 7967.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityQuantile90[9, 14] == 9766.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityPGround[9, 14] == 0.0F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityPCumulativeZQ10[9, 14] == 0.115006164F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityPCumulativeZQ30[9, 14] == 0.294207036F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityPCumulativeZQ50[9, 14] == 0.467282623F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityPCumulativeZQ70[9, 14] == 0.65351975F);
            Assert.IsTrue(metricsRasterMonolithic.IntensityPCumulativeZQ90[9, 14] == 0.868495464F);
            Assert.IsTrue(metricsRasterMonolithic.PFirstReturn[9, 14] == 0.0F);
            Assert.IsTrue(metricsRasterMonolithic.PSecondReturn[9, 14] == 0.0F);
            Assert.IsTrue(metricsRasterMonolithic.PThirdReturn[9, 14] == 0.0F);
            Assert.IsTrue(metricsRasterMonolithic.PFourthReturn[9, 14] == 0.0F);
            Assert.IsTrue(metricsRasterMonolithic.PFifthReturn[9, 14] == 0.0F);
            Assert.IsTrue(metricsRasterMonolithic.PGround[9, 14] == 0.0F);

            Assert.IsTrue(metricsRasterMonolithic.AcceptedPoints[8, 15] == 0);
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZMax[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZMean[8, 15]));
            Assert.IsTrue(metricsRasterMonolithic.ZGroundMean[8, 15] == 921.348267F);
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZStandardDeviation[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZSkew[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZKurtosis[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZNormalizedEntropy[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.PZAboveZMean[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.PZAboveThreshold[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile05[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile10[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile15[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile20[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile25[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile30[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile35[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile40[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile45[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile50[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile55[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile60[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile65[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile70[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile75[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile80[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile85[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile90[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile95[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZPCumulative10[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZPCumulative20[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZPCumulative30[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZPCumulative40[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZPCumulative50[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZPCumulative60[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZPCumulative70[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZPCumulative80[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZPCumulative90[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityFirstReturn[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityMean[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityMeanAboveMedianZ[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityMeanBelowMedianZ[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityMax[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityStandardDeviation[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensitySkew[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityKurtosis[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityTotal[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityQuantile10[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityQuantile20[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityQuantile30[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityQuantile40[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityQuantile50[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityQuantile60[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityQuantile70[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityQuantile80[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityQuantile90[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityPGround[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityPCumulativeZQ10[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityPCumulativeZQ30[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityPCumulativeZQ50[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityPCumulativeZQ70[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityPCumulativeZQ90[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.PFirstReturn[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.PSecondReturn[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.PThirdReturn[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.PFourthReturn[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.PFifthReturn[8, 15]));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.PGround[8, 15]));

            Assert.IsTrue(metricsRasterMonolithic.AcceptedPoints[9, 15] == 0);
            // for now, remaining bands aren't checked for NaN as coverage of previous cell should suffice

            Assert.IsTrue(metricsRasterMonolithic.AcceptedPoints.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZMax.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZMean.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZGroundMean.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZStandardDeviation.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZSkew.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZKurtosis.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZNormalizedEntropy.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.PZAboveZMean.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.PZAboveThreshold.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile05.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile10.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile15.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile20.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile25.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile30.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile35.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile40.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile45.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile50.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile55.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile60.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile65.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile70.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile75.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile80.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile85.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile90.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZQuantile95.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative10.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative20.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative30.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative40.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative50.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative60.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative70.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative80.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.ZPCumulative90.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.IntensityFirstReturn.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.IntensityMean.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.IntensityMeanAboveMedianZ.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.IntensityMeanBelowMedianZ.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.IntensityMax.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.IntensityStandardDeviation.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.IntensitySkew.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.IntensityKurtosis.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.IntensityTotal.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.IntensityPGround.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.IntensityPCumulativeZQ10.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.IntensityPCumulativeZQ30.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.IntensityPCumulativeZQ50.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.IntensityPCumulativeZQ70.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.IntensityPCumulativeZQ90.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.PFirstReturn.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.PSecondReturn.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.PThirdReturn.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.PFourthReturn.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.PFifthReturn.HasNoDataValue);
            Assert.IsTrue(metricsRasterMonolithic.PGround.HasNoDataValue);

            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.AcceptedPoints.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZMax.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZMean.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZGroundMean.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZStandardDeviation.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZSkew.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZKurtosis.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZNormalizedEntropy.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.PZAboveZMean.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.PZAboveThreshold.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile05.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile10.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile15.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile20.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile25.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile30.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile35.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile40.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile45.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile50.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile55.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile60.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile65.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile70.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile75.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile80.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile85.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile90.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZQuantile95.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZPCumulative10.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZPCumulative20.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZPCumulative30.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZPCumulative40.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZPCumulative50.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZPCumulative60.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZPCumulative70.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZPCumulative80.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.ZPCumulative90.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityFirstReturn.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityMean.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityMeanAboveMedianZ.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityMeanBelowMedianZ.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityMax.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityStandardDeviation.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensitySkew.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityKurtosis.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityTotal.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityPGround.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityPCumulativeZQ10.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityPCumulativeZQ30.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityPCumulativeZQ50.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityPCumulativeZQ70.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.IntensityPCumulativeZQ90.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.PFirstReturn.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.PSecondReturn.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.PThirdReturn.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.PFourthReturn.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.PFifthReturn.NoDataValue));
            Assert.IsTrue(Single.IsNaN(metricsRasterMonolithic.PGround.NoDataValue));
        }

        [TestMethod]
        public void ReadLasToImage()
        {
            Debug.Assert(this.UnitTestPath != null);
            LasTile lasTile = this.ReadLasTile();

            double imageCellSize = 0.5;
            int imageXsize = (int)(lasTile.GridExtent.Width / imageCellSize) + 1; // 3.6 x 4.2 m -> 4.0 x 4.5 m
            int imageYsize = (int)(lasTile.GridExtent.Height / imageCellSize) + 1;
            GridGeoTransform imageTransform = new(lasTile.GridExtent, imageCellSize, imageCellSize);

            byte[]? pointReadBuffer = null;
            ImageRaster<UInt64> image = new(lasTile.GetSpatialReference(), imageTransform, imageXsize, imageYsize, includeNearInfrared: false);
            using LasReader imageReader = lasTile.CreatePointReader(unbuffered: false, enableAsync: false);
            imageReader.ReadPointsToImage(lasTile, image, ref pointReadBuffer);
            image.OnPointAdditionComplete();

            Assert.IsTrue((image.Cells == 72) && (image.SizeX == imageXsize) && (image.SizeY == imageYsize) && (image.NearInfrared == null));
            for (int yIndex = 0; yIndex < image.SizeY; ++yIndex)
            {
                for (int xIndex = 0; xIndex < image.SizeX; ++xIndex)
                {
                    // test data uses point format 6: no RGB or NIR, only intensity 
                    // Since all pixels have points, data values get set to zero even though there's no data. Intensity is ignored
                    // as the test .las isn't LAS 1.4 compliant, having return numbers all set to zero.
                    Assert.IsTrue(image.Red[xIndex, yIndex] == UInt64.MaxValue);
                    Assert.IsTrue(image.Green[xIndex, yIndex] == UInt64.MaxValue);
                    Assert.IsTrue(image.Blue[xIndex, yIndex] == UInt64.MaxValue);
                    // image.NearInfrared[xIndex, yIndex] is null
                    Assert.IsTrue(image.IntensityFirstReturn[xIndex, yIndex] == UInt64.MaxValue);
                    Assert.IsTrue(image.IntensitySecondReturn[xIndex, yIndex] == UInt64.MaxValue);
                    Assert.IsTrue(image.FirstReturns[xIndex, yIndex] == 0);
                    Assert.IsTrue(image.SecondReturns[xIndex, yIndex] == 0);
                }
            }
        }

        [TestMethod]
        public void ReadLasToScanMetrics()
        {
            Debug.Assert(this.UnitTestPath != null);
            LasTile lasTile = this.ReadLasTile();

            Raster<UInt16> gridCellDefinitions = this.SnapPointCloudTileToGridCells(lasTile);
            ScanMetricsRaster scanMetrics = new(gridCellDefinitions);

            byte[]? pointReadBuffer = null;
            using LasReader pointReader = lasTile.CreatePointReader();
            pointReader.ReadPointsToGrid(lasTile, scanMetrics, ref pointReadBuffer);
            scanMetrics.OnPointAdditionComplete();

            Assert.IsTrue(scanMetrics.Cells == 575);
            Assert.IsTrue(scanMetrics.SizeX == 23);
            Assert.IsTrue(scanMetrics.SizeY == 25);

            Assert.IsTrue(scanMetrics.EdgeOfFlightLine.IsNoData(UInt32.MaxValue));
            Assert.IsTrue(scanMetrics.NoiseOrWithheld.IsNoData(UInt32.MaxValue));
            Assert.IsTrue(scanMetrics.Overlap.IsNoData(UInt32.MaxValue));
            Assert.IsTrue(scanMetrics.GpstimeMax.IsNoData(Double.NaN));
            Assert.IsTrue(scanMetrics.GpstimeMean.IsNoData(Double.NaN));
            Assert.IsTrue(scanMetrics.GpstimeMin.IsNoData(Double.NaN));
            Assert.IsTrue(scanMetrics.ScanAngleMin.IsNoData(Single.NaN));
            Assert.IsTrue(scanMetrics.ScanAngleMeanAbsolute.IsNoData(Single.NaN));
            Assert.IsTrue(scanMetrics.ScanAngleMax.IsNoData(Single.NaN));

            for (int yIndex = 0; yIndex < scanMetrics.SizeY; ++yIndex)
            {
                for (int xIndex = 0; xIndex < scanMetrics.SizeX; ++xIndex) 
                {
                    if (yIndex == 14)
                    {
                        if (xIndex == 8)
                        {
                            Assert.IsTrue(scanMetrics.AcceptedPoints[xIndex, yIndex] == 162764.0);
                            Assert.IsTrue(scanMetrics.ScanAngleMeanAbsolute[xIndex, yIndex] == 0.0); // fields not populated in test data
                            Assert.IsTrue(scanMetrics.ScanDirectionMean[xIndex, yIndex] == 0.0);
                            Assert.IsTrue(scanMetrics.ScanAngleMin[xIndex, yIndex] == 0.0);
                            Assert.IsTrue(scanMetrics.ScanAngleMax[xIndex, yIndex] == 0.0);
                            Assert.IsTrue(scanMetrics.NoiseOrWithheld[xIndex, yIndex] == 0.0);
                            Assert.IsTrue(scanMetrics.EdgeOfFlightLine[xIndex, yIndex] == 0.0);
                            Assert.IsTrue(scanMetrics.Overlap[xIndex, yIndex] == 0.0);
                            Assert.IsTrue(scanMetrics.GpstimeMin[xIndex, yIndex] == 1688896198.4103589);
                            Assert.IsTrue(scanMetrics.GpstimeMean[xIndex, yIndex] == 1688896288.6329067);
                            Assert.IsTrue(scanMetrics.GpstimeMax[xIndex, yIndex] == 1688896434.3628883);

                            continue;
                        }
                        else if (xIndex == 9)
                        {
                            Assert.IsTrue(scanMetrics.AcceptedPoints[xIndex, yIndex] == 44853.0);
                            Assert.IsTrue(scanMetrics.ScanAngleMeanAbsolute[xIndex, yIndex] == 0.0);
                            Assert.IsTrue(scanMetrics.ScanDirectionMean[xIndex, yIndex] == 0.0);
                            Assert.IsTrue(scanMetrics.ScanAngleMin[xIndex, yIndex] == 0.0);
                            Assert.IsTrue(scanMetrics.ScanAngleMax[xIndex, yIndex] == 0.0);
                            Assert.IsTrue(scanMetrics.NoiseOrWithheld[xIndex, yIndex] == 0.0);
                            Assert.IsTrue(scanMetrics.EdgeOfFlightLine[xIndex, yIndex] == 0.0);
                            Assert.IsTrue(scanMetrics.Overlap[xIndex, yIndex] == 0.0);
                            Assert.IsTrue(scanMetrics.GpstimeMin[xIndex, yIndex] == 1688896204.9299486);
                            Assert.IsTrue(scanMetrics.GpstimeMean[xIndex, yIndex] == 1688896285.2203009);
                            Assert.IsTrue(scanMetrics.GpstimeMax[xIndex, yIndex] == 1688896383.909029);

                            continue;
                        }
                    }

                    Assert.IsTrue(scanMetrics.AcceptedPoints[xIndex, yIndex] == 0);
                    Assert.IsTrue(Single.IsNaN(scanMetrics.ScanAngleMeanAbsolute[xIndex, yIndex]));
                    Assert.IsTrue(Single.IsNaN(scanMetrics.ScanDirectionMean[xIndex, yIndex]));
                    Assert.IsTrue(Single.IsNaN(scanMetrics.ScanAngleMin[xIndex, yIndex]));
                    Assert.IsTrue(Single.IsNaN(scanMetrics.ScanAngleMax[xIndex, yIndex]));
                    Assert.IsTrue(scanMetrics.NoiseOrWithheld[xIndex, yIndex] == 0);
                    Assert.IsTrue(scanMetrics.EdgeOfFlightLine[xIndex, yIndex] == 0);
                    Assert.IsTrue(scanMetrics.Overlap[xIndex, yIndex] == 0);
                    Assert.IsTrue(Double.IsNaN(scanMetrics.GpstimeMin[xIndex, yIndex]));
                    Assert.IsTrue(Double.IsNaN(scanMetrics.GpstimeMean[xIndex, yIndex]));
                    Assert.IsTrue(Double.IsNaN(scanMetrics.GpstimeMax[xIndex, yIndex]));
                }
            }
        }

        private LasTile ReadLasTile()
        {
            Debug.Assert(this.UnitTestPath != null);

            FileInfo lasFileInfo = new(Path.Combine(this.UnitTestPath, TestConstant.LasFileName));
            using FileStream stream = new(lasFileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, LasReader.HeaderAndVlrReadBufferSizeInBytes);
            using LasReader headerVlrReader = new(stream);
            LasTile lasTile = new(lasFileInfo.FullName, headerVlrReader, fallbackCreationDate: null);

            return lasTile;
        }

        public Raster<UInt16> SnapPointCloudTileToGridCells(LasTile lasTile)
        {
            Debug.Assert(this.UnitTestPath != null);

            // bypass LasTileGrid.Create(lasTiles, 32610) as test point cloud is much smaller than a full LiDAR/SfM tile
            // Since a tile smaller than an ABA cell is an error, the test path is to boost the tile's extent to be the size of an ABA
            // cell and set the point cloud tile grid pitch matches the expanded tile size.
            using Dataset gridCellDefinitionDataset = Gdal.Open(Path.Combine(this.UnitTestPath, "PSME ABA grid cells.tif"), Access.GA_ReadOnly);
            Raster<UInt16>  gridCellDefinitions = new(gridCellDefinitionDataset, readData: true); // data needed to check for no data values
            lasTile.GridExtent.XMax = lasTile.GridExtent.XMin + gridCellDefinitions.Transform.CellWidth;
            lasTile.GridExtent.YMin = lasTile.GridExtent.YMax + gridCellDefinitions.Transform.CellHeight; // cell height is negative

            return gridCellDefinitions;
        }
    }
}