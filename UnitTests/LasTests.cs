using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using MaxRev.Gdal.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OSGeo.GDAL;
using OSGeo.OSR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Mars.Clouds.UnitTests
{
    [TestClass]
    public class LasTests
    {
        private string? unitTestPath;

        public TestContext? TestContext { get; set; }

        [TestInitialize]
        public void TestInitialize()
        {
            this.unitTestPath = Path.Combine(this.TestContext!.TestRunDirectory!, "..\\..\\UnitTests");

            GdalBase.ConfigureAll();
        }

        [TestMethod]
        public void ReadLas()
        {
            Debug.Assert(this.unitTestPath != null);

            FileInfo lasFileInfo = new(Path.Combine(this.unitTestPath, "PSME LAS 1.4 point type 6.las"));
            using FileStream stream = new(lasFileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024);
            using LasReader headerVlrReader = new(stream);
            LasTile lasTile = new(lasFileInfo.FullName, headerVlrReader);

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
            Assert.IsTrue(lasHeader14.OffsetToPointData == 1131);
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
            GeoKeyDirectoryTagRecord crs = (GeoKeyDirectoryTagRecord)lasTile.VariableLengthRecords[0];
            Assert.IsTrue(crs.Reserved == 43707); // bug in originating writer; should be zero
            Assert.IsTrue(String.Equals(crs.UserID, "LASF_Projection", StringComparison.Ordinal));
            Assert.IsTrue(crs.RecordID == GeoKeyDirectoryTagRecord.LasfProjectionRecordID);
            Assert.IsTrue(crs.RecordLengthAfterHeader == 48);
            Assert.IsTrue(String.Equals(crs.Description, "Geotiff Projection Keys", StringComparison.Ordinal));
            Assert.IsTrue(crs.KeyDirectoryVersion == 1);
            Assert.IsTrue(crs.KeyRevision == 1);
            Assert.IsTrue(crs.MinorRevision == 0);
            Assert.IsTrue(crs.NumberOfKeys == 3); // bug in originating writer, should be two
            Assert.IsTrue(crs.KeyEntries.Count == crs.NumberOfKeys);
            GeoKeyEntry projectedCrsKey = crs.KeyEntries[0];
            Assert.IsTrue(projectedCrsKey.KeyID == GeoKey.ProjectedCSTypeGeoKey);
            Assert.IsTrue(projectedCrsKey.TiffTagLocation == 0);
            Assert.IsTrue(projectedCrsKey.Count == 1);
            Assert.IsTrue(projectedCrsKey.ValueOrOffset == 32610);
            GeoKeyEntry verticalCrsKey = crs.KeyEntries[1];
            Assert.IsTrue(verticalCrsKey.KeyID == GeoKey.VerticalUnitsGeoKey);
            Assert.IsTrue(verticalCrsKey.TiffTagLocation == 0);
            Assert.IsTrue(verticalCrsKey.Count == 1);
            Assert.IsTrue(verticalCrsKey.ValueOrOffset == 32610);
            // third key is a write error and is all zero; ignore for now

            int lasFileEpsg = lasTile.GetProjectedCoordinateSystemEpsg();
            Assert.IsTrue(lasFileEpsg == 32610);

            // bypass LasTileGrid.Create(lasTiles, 32610) as test point cloud is much smaller than a full LiDAR/SfM tile
            // Since a tile smaller than an ABA cell is an error, the test path is to boost the tile's extent to be the size of an ABA
            // cell and set the LAS tile grid pitch matches the expanded tile size.
            using Dataset gridCellDefinitionDataset = Gdal.Open(Path.Combine(this.unitTestPath, "PSME ABA grid cells.tif"), Access.GA_ReadOnly);
            Raster<UInt16> gridCellDefinitions = new(gridCellDefinitionDataset);
            lasTile.GridExtent.XMax = lasTile.GridExtent.XMin + gridCellDefinitions.Transform.CellWidth;
            lasTile.GridExtent.YMin = lasTile.GridExtent.YMax + gridCellDefinitions.Transform.CellHeight; // cell height is negative
            GridGeoTransform lasFileTransform = new(lasTile.GridExtent.XMin, lasTile.GridExtent.YMax, gridCellDefinitions.Transform.CellWidth, gridCellDefinitions.Transform.CellHeight);
            LasTileGrid lasGrid = new(lasTile.GetSpatialReference(), lasFileTransform, 1, 1, new List<LasTile>() { lasTile });

            AbaGrid abaGrid = new(gridCellDefinitions, 0, lasGrid);

            Assert.IsTrue(gridCellDefinitions.Crs.IsSame(abaGrid.Crs, []) == 1);
            Assert.IsTrue(GridGeoTransform.Equals(gridCellDefinitions.Transform, abaGrid.Transform));
            Assert.IsTrue(gridCellDefinitions.XSize == abaGrid.XSize);
            Assert.IsTrue(gridCellDefinitions.YSize == abaGrid.YSize);
            Assert.IsTrue(Int32.Parse(abaGrid.Crs.GetAuthorityCode("PROJCS")) == 32610);
            int populatedGridCells = 0;
            for (int yIndex = 0; yIndex < abaGrid.YSize; ++yIndex)
            {
                for (int xIndex = 0; xIndex < abaGrid.XSize; ++xIndex)
                {
                    if (abaGrid[xIndex, yIndex] != null)
                    {
                        ++populatedGridCells;
                    }
                }
            }
            Assert.IsTrue(populatedGridCells == 4); // two cells with points (8, 14 and 9, 14) plus two cells without points due to proxied tile size (8, 15 and 9, 15)

            using LasReader pointReader = lasTile.CreatePointReader();
            headerVlrReader.ReadPointsToGridZirn(lasTile, abaGrid);
            PointListZirnc? psmeCell1 = abaGrid[8, 14];
            Assert.IsTrue(psmeCell1 != null);
            Assert.IsTrue(psmeCell1.TilesIntersected == 1);
            Assert.IsTrue(psmeCell1.TilesLoaded == 1);
            Assert.IsTrue(psmeCell1.XIndex == 8);
            Assert.IsTrue(psmeCell1.YIndex == 14);
            Assert.IsTrue(psmeCell1.Count == 162764);
            Assert.IsTrue(psmeCell1.Z.Count == psmeCell1.Count);
            Assert.IsTrue(psmeCell1.Intensity.Count == psmeCell1.Count);
            Assert.IsTrue(psmeCell1.ReturnNumber.Count == psmeCell1.Count);
            Assert.IsTrue(psmeCell1.Classification.Count == psmeCell1.Count);

            PointListZirnc? psmeCell2 = abaGrid[9, 14];
            Assert.IsTrue(psmeCell2 != null);
            Assert.IsTrue(psmeCell2.TilesIntersected == 1);
            Assert.IsTrue(psmeCell2.TilesLoaded == 1);
            Assert.IsTrue(psmeCell2.XIndex == 9);
            Assert.IsTrue(psmeCell2.YIndex == 14);
            Assert.IsTrue(psmeCell2.Count == 44853);
            Assert.IsTrue(psmeCell2.Z.Count == psmeCell2.Count);
            Assert.IsTrue(psmeCell2.Intensity.Count == psmeCell2.Count);
            Assert.IsTrue(psmeCell2.ReturnNumber.Count == psmeCell2.Count);
            Assert.IsTrue(psmeCell2.Classification.Count == psmeCell2.Count);

            PointListZirnc? adjacentCell1 = abaGrid[8, 15];
            Assert.IsTrue(adjacentCell1 != null);
            Assert.IsTrue(adjacentCell1.TilesIntersected == 1);
            Assert.IsTrue(adjacentCell1.TilesLoaded == 1);
            Assert.IsTrue(adjacentCell1.XIndex == 8);
            Assert.IsTrue(adjacentCell1.YIndex == 15);
            Assert.IsTrue(adjacentCell1.Count == 0);

            PointListZirnc? adjacentCell2 = abaGrid[9, 15];
            Assert.IsTrue(adjacentCell2 != null);
            Assert.IsTrue(adjacentCell2.TilesIntersected == 1);
            Assert.IsTrue(adjacentCell2.TilesLoaded == 1);
            Assert.IsTrue(adjacentCell2.XIndex == 9);
            Assert.IsTrue(adjacentCell2.YIndex == 15);
            Assert.IsTrue(adjacentCell2.Count == 0);

            SpatialReference lasSpatialReference = lasTile.GetSpatialReference();
            float crsLinearUnits = (float)lasSpatialReference.GetLinearUnits();
            float oneMeterHeightClass = 1.0F / crsLinearUnits;
            float twoMeterHeightThreshold = 920.52F + 2.0F / crsLinearUnits;
            StandardMetricsRaster abaMetrics = new(abaGrid.Crs, abaGrid.Transform, abaGrid.XSize, abaGrid.YSize);
            psmeCell1.GetStandardMetrics(abaMetrics, oneMeterHeightClass, twoMeterHeightThreshold);
            psmeCell2.GetStandardMetrics(abaMetrics, oneMeterHeightClass, twoMeterHeightThreshold);
            adjacentCell1.GetStandardMetrics(abaMetrics, oneMeterHeightClass, twoMeterHeightThreshold);
            adjacentCell2.GetStandardMetrics(abaMetrics, oneMeterHeightClass, twoMeterHeightThreshold);

            Assert.IsTrue(abaMetrics.N[8, 14] == 162764);
            Assert.IsTrue(abaMetrics.ZMax[8, 14] == 928.6306F);
            Assert.IsTrue(abaMetrics.ZMean[8, 14] == 923.698242F);
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZGroundMean[8, 14])); // points not classified
            Assert.IsTrue(abaMetrics.ZStandardDeviation[8, 14] == 1.69425166F);
            Assert.IsTrue(abaMetrics.ZSkew[8, 14] == 0.3093502F);
            Assert.IsTrue(abaMetrics.ZKurtosis[8, 14] == 2.619736F);
            Assert.IsTrue(abaMetrics.ZNormalizedEntropy[8, 14] == 0.83192575F);
            Assert.IsTrue(abaMetrics.PZAboveZMean[8, 14] == 0.4679106F);
            Assert.IsTrue(abaMetrics.PZAboveThreshold[8, 14] == 0.740716636F);
            Assert.IsTrue(abaMetrics.ZQuantile05[8, 14] == 920.903137F);
            Assert.IsTrue(abaMetrics.ZQuantile10[8, 14] == 921.4857F);
            Assert.IsTrue(abaMetrics.ZQuantile15[8, 14] == 921.985352F);
            Assert.IsTrue(abaMetrics.ZQuantile20[8, 14] == 922.275F);
            Assert.IsTrue(abaMetrics.ZQuantile25[8, 14] == 922.4862F);
            Assert.IsTrue(abaMetrics.ZQuantile30[8, 14] == 922.6903F);
            Assert.IsTrue(abaMetrics.ZQuantile35[8, 14] == 922.8859F);
            Assert.IsTrue(abaMetrics.ZQuantile40[8, 14] == 923.052551F);
            Assert.IsTrue(abaMetrics.ZQuantile45[8, 14] == 923.2003F);
            Assert.IsTrue(abaMetrics.ZQuantile50[8, 14] == 923.4795F);
            Assert.IsTrue(abaMetrics.ZQuantile55[8, 14] == 923.8576F);
            Assert.IsTrue(abaMetrics.ZQuantile60[8, 14] == 924.2154F);
            Assert.IsTrue(abaMetrics.ZQuantile65[8, 14] == 924.2943F);
            Assert.IsTrue(abaMetrics.ZQuantile70[8, 14] == 924.5219F);
            Assert.IsTrue(abaMetrics.ZQuantile75[8, 14] == 924.852234F);
            Assert.IsTrue(abaMetrics.ZQuantile80[8, 14] == 925.0855F);
            Assert.IsTrue(abaMetrics.ZQuantile85[8, 14] == 925.5673F);
            Assert.IsTrue(abaMetrics.ZQuantile90[8, 14] == 925.9668F);
            Assert.IsTrue(abaMetrics.ZQuantile95[8, 14] == 926.7014F);
            Assert.IsTrue(abaMetrics.ZPCumulative10[8, 14] == 0.08913519F);
            Assert.IsTrue(abaMetrics.ZPCumulative20[8, 14] == 0.181962848F);
            Assert.IsTrue(abaMetrics.ZPCumulative30[8, 14] == 0.3759185F);
            Assert.IsTrue(abaMetrics.ZPCumulative40[8, 14] == 0.5440515F);
            Assert.IsTrue(abaMetrics.ZPCumulative50[8, 14] == 0.708209455F);
            Assert.IsTrue(abaMetrics.ZPCumulative60[8, 14] == 0.8307365F);
            Assert.IsTrue(abaMetrics.ZPCumulative70[8, 14] == 0.911983F);
            Assert.IsTrue(abaMetrics.ZPCumulative80[8, 14] == 0.958602667F);
            Assert.IsTrue(abaMetrics.ZPCumulative90[8, 14] == 0.9956993F);
            Assert.IsTrue(abaMetrics.IntensityTotal[8, 14] == 9.244475E+08F);
            Assert.IsTrue(abaMetrics.IntensityMax[8, 14] == 29812.0F);
            Assert.IsTrue(abaMetrics.IntensityMean[8, 14] == 5679.68066F);
            Assert.IsTrue(abaMetrics.IntensityStandardDeviation[8, 14] == 3675.354F);
            Assert.IsTrue(abaMetrics.IntensitySkew[8, 14] == 0.610383332F);
            Assert.IsTrue(abaMetrics.IntensityKurtosis[8, 14] == 2.95583248F);
            Assert.IsTrue(abaMetrics.IntensityPGround[8, 14] == 0.0F); // points not classified
            Assert.IsTrue(abaMetrics.IntensityPCumulativeZQ10[8, 14] == 0.102303207F);
            Assert.IsTrue(abaMetrics.IntensityPCumulativeZQ30[8, 14] == 0.2589203F);
            Assert.IsTrue(abaMetrics.IntensityPCumulativeZQ50[8, 14] == 0.44931823F);
            Assert.IsTrue(abaMetrics.IntensityPCumulativeZQ70[8, 14] == 0.6531651F);
            Assert.IsTrue(abaMetrics.IntensityPCumulativeZQ90[8, 14] == 0.8864496F);
            Assert.IsTrue(abaMetrics.PFirstReturn[8, 14] == 0.0F); // defect in manufacturer proprietary software generating point cloud: no return numbers set
            Assert.IsTrue(abaMetrics.PSecondReturn[8, 14] == 0.0F);
            Assert.IsTrue(abaMetrics.PThirdReturn[8, 14] == 0.0F);
            Assert.IsTrue(abaMetrics.PFourthReturn[8, 14] == 0.0F);
            Assert.IsTrue(abaMetrics.PFifthReturn[8, 14] == 0.0F);
            Assert.IsTrue(abaMetrics.PGround[8, 14] == 0.0F); // points not classified
            Assert.IsTrue(abaMetrics.AreaOfPointBoundingBox[8, 14] == 11.5402412F);

            Assert.IsTrue(abaMetrics.N[9, 14] == 44853.0F);
            Assert.IsTrue(abaMetrics.ZMax[9, 14] == 926.576538F);
            Assert.IsTrue(abaMetrics.ZMean[9, 14] == 922.74585F);
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZGroundMean[9, 14]));
            Assert.IsTrue(abaMetrics.ZStandardDeviation[9, 14] == 1.39960063F);
            Assert.IsTrue(abaMetrics.ZSkew[9, 14] == 0.329364121F);
            Assert.IsTrue(abaMetrics.ZKurtosis[9, 14] == 2.12710214F);
            Assert.IsTrue(abaMetrics.ZNormalizedEntropy[9, 14] == 0.828225255F);
            Assert.IsTrue(abaMetrics.PZAboveZMean[9, 14] == 0.454975128F);
            Assert.IsTrue(abaMetrics.PZAboveThreshold[9, 14] == 0.5062315F);
            Assert.IsTrue(abaMetrics.ZQuantile05[9, 14] == 920.749F);
            Assert.IsTrue(abaMetrics.ZQuantile10[9, 14] == 920.872437F);
            Assert.IsTrue(abaMetrics.ZQuantile15[9, 14] == 921.17804F);
            Assert.IsTrue(abaMetrics.ZQuantile20[9, 14] == 921.4686F);
            Assert.IsTrue(abaMetrics.ZQuantile25[9, 14] == 921.5838F);
            Assert.IsTrue(abaMetrics.ZQuantile30[9, 14] == 921.74884F);
            Assert.IsTrue(abaMetrics.ZQuantile35[9, 14] == 921.9464F);
            Assert.IsTrue(abaMetrics.ZQuantile40[9, 14] == 922.1297F);
            Assert.IsTrue(abaMetrics.ZQuantile45[9, 14] == 922.2642F);
            Assert.IsTrue(abaMetrics.ZQuantile50[9, 14] == 922.537537F);
            Assert.IsTrue(abaMetrics.ZQuantile55[9, 14] == 922.775452F);
            Assert.IsTrue(abaMetrics.ZQuantile60[9, 14] == 923.1283F);
            Assert.IsTrue(abaMetrics.ZQuantile65[9, 14] == 923.2835F);
            Assert.IsTrue(abaMetrics.ZQuantile70[9, 14] == 923.620544F);
            Assert.IsTrue(abaMetrics.ZQuantile75[9, 14] == 923.9276F);
            Assert.IsTrue(abaMetrics.ZQuantile80[9, 14] == 924.252F);
            Assert.IsTrue(abaMetrics.ZQuantile85[9, 14] == 924.3579F);
            Assert.IsTrue(abaMetrics.ZQuantile90[9, 14] == 924.6782F);
            Assert.IsTrue(abaMetrics.ZQuantile95[9, 14] == 925.027832F);
            Assert.IsTrue(abaMetrics.ZPCumulative10[9, 14] == 0.140503421F);
            Assert.IsTrue(abaMetrics.ZPCumulative20[9, 14] == 0.295008123F);
            Assert.IsTrue(abaMetrics.ZPCumulative30[9, 14] == 0.4641607F);
            Assert.IsTrue(abaMetrics.ZPCumulative40[9, 14] == 0.569215F);
            Assert.IsTrue(abaMetrics.ZPCumulative50[9, 14] == 0.688471258F);
            Assert.IsTrue(abaMetrics.ZPCumulative60[9, 14] == 0.7677524F);
            Assert.IsTrue(abaMetrics.ZPCumulative70[9, 14] == 0.907653868F);
            Assert.IsTrue(abaMetrics.ZPCumulative80[9, 14] == 0.972644F);
            Assert.IsTrue(abaMetrics.ZPCumulative90[9, 14] == 0.993043959F);
            Assert.IsTrue(abaMetrics.IntensityTotal[9, 14] == 222583584.0F);
            Assert.IsTrue(abaMetrics.IntensityMax[9, 14] == 27242.0F);
            Assert.IsTrue(abaMetrics.IntensityMean[9, 14] == 4962.5127F);
            Assert.IsTrue(abaMetrics.IntensityStandardDeviation[9, 14] == 3340.16382F);
            Assert.IsTrue(abaMetrics.IntensitySkew[9, 14] == 0.6552501F);
            Assert.IsTrue(abaMetrics.IntensityKurtosis[9, 14] == 2.89136815F);
            Assert.IsTrue(abaMetrics.IntensityPGround[9, 14] == 0.0F);
            Assert.IsTrue(abaMetrics.IntensityPCumulativeZQ10[9, 14] == 0.115006164F);
            Assert.IsTrue(abaMetrics.IntensityPCumulativeZQ30[9, 14] == 0.294207036F);
            Assert.IsTrue(abaMetrics.IntensityPCumulativeZQ50[9, 14] == 0.467282623F);
            Assert.IsTrue(abaMetrics.IntensityPCumulativeZQ70[9, 14] == 0.65351975F);
            Assert.IsTrue(abaMetrics.IntensityPCumulativeZQ90[9, 14] == 0.868495464F);
            Assert.IsTrue(abaMetrics.PFirstReturn[9, 14] == 0.0F);
            Assert.IsTrue(abaMetrics.PSecondReturn[9, 14] == 0.0F);
            Assert.IsTrue(abaMetrics.PThirdReturn[9, 14] == 0.0F);
            Assert.IsTrue(abaMetrics.PFourthReturn[9, 14] == 0.0F);
            Assert.IsTrue(abaMetrics.PFifthReturn[9, 14] == 0.0F);
            Assert.IsTrue(abaMetrics.PGround[9, 14] == 0.0F);
            Assert.IsTrue(abaMetrics.AreaOfPointBoundingBox[9, 14] == 3.70189786F);

            Assert.IsTrue(abaMetrics.N[8, 15] == 0);
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZMax[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZMean[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZGroundMean[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZStandardDeviation[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZSkew[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZKurtosis[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZNormalizedEntropy[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.PZAboveZMean[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.PZAboveThreshold[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile05[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile10[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile15[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile20[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile25[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile30[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile35[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile40[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile45[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile50[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile55[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile60[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile65[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile70[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile75[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile80[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile85[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile90[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile95[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZPCumulative10[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZPCumulative20[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZPCumulative30[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZPCumulative40[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZPCumulative50[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZPCumulative60[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZPCumulative70[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZPCumulative80[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZPCumulative90[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityTotal[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityMax[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityMean[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityStandardDeviation[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensitySkew[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityKurtosis[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityPGround[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityPCumulativeZQ10[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityPCumulativeZQ30[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityPCumulativeZQ50[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityPCumulativeZQ70[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityPCumulativeZQ90[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.PFirstReturn[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.PSecondReturn[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.PThirdReturn[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.PFourthReturn[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.PFifthReturn[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.PGround[8, 15]));
            Assert.IsTrue(Single.IsNaN(abaMetrics.AreaOfPointBoundingBox[8, 15]));

            Assert.IsTrue(abaMetrics.N[9, 15] == 0);
            // for now, remaining bands aren't checked for NaN as coverage of previous cell should suffice

            Assert.IsTrue(abaMetrics.N.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZMax.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZMean.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZGroundMean.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZStandardDeviation.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZSkew.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZKurtosis.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZNormalizedEntropy.HasNoDataValue);
            Assert.IsTrue(abaMetrics.PZAboveZMean.HasNoDataValue);
            Assert.IsTrue(abaMetrics.PZAboveThreshold.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZQuantile05.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZQuantile10.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZQuantile15.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZQuantile20.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZQuantile25.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZQuantile30.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZQuantile35.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZQuantile40.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZQuantile45.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZQuantile50.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZQuantile55.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZQuantile60.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZQuantile65.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZQuantile70.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZQuantile75.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZQuantile80.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZQuantile85.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZQuantile90.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZQuantile95.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZPCumulative10.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZPCumulative20.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZPCumulative30.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZPCumulative40.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZPCumulative50.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZPCumulative60.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZPCumulative70.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZPCumulative80.HasNoDataValue);
            Assert.IsTrue(abaMetrics.ZPCumulative90.HasNoDataValue);
            Assert.IsTrue(abaMetrics.IntensityTotal.HasNoDataValue);
            Assert.IsTrue(abaMetrics.IntensityMax.HasNoDataValue);
            Assert.IsTrue(abaMetrics.IntensityMean.HasNoDataValue);
            Assert.IsTrue(abaMetrics.IntensityStandardDeviation.HasNoDataValue);
            Assert.IsTrue(abaMetrics.IntensitySkew.HasNoDataValue);
            Assert.IsTrue(abaMetrics.IntensityKurtosis.HasNoDataValue);
            Assert.IsTrue(abaMetrics.IntensityPGround.HasNoDataValue);
            Assert.IsTrue(abaMetrics.IntensityPCumulativeZQ10.HasNoDataValue);
            Assert.IsTrue(abaMetrics.IntensityPCumulativeZQ30.HasNoDataValue);
            Assert.IsTrue(abaMetrics.IntensityPCumulativeZQ50.HasNoDataValue);
            Assert.IsTrue(abaMetrics.IntensityPCumulativeZQ70.HasNoDataValue);
            Assert.IsTrue(abaMetrics.IntensityPCumulativeZQ90.HasNoDataValue);
            Assert.IsTrue(abaMetrics.PFirstReturn.HasNoDataValue);
            Assert.IsTrue(abaMetrics.PSecondReturn.HasNoDataValue);
            Assert.IsTrue(abaMetrics.PThirdReturn.HasNoDataValue);
            Assert.IsTrue(abaMetrics.PFourthReturn.HasNoDataValue);
            Assert.IsTrue(abaMetrics.PFifthReturn.HasNoDataValue);
            Assert.IsTrue(abaMetrics.PGround.HasNoDataValue);
            Assert.IsTrue(abaMetrics.AreaOfPointBoundingBox.HasNoDataValue);

            Assert.IsTrue(Single.IsNaN(abaMetrics.N.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZMax.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZMean.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZGroundMean.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZStandardDeviation.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZSkew.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZKurtosis.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZNormalizedEntropy.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.PZAboveZMean.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.PZAboveThreshold.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile05.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile10.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile15.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile20.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile25.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile30.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile35.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile40.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile45.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile50.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile55.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile60.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile65.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile70.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile75.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile80.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile85.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile90.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZQuantile95.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZPCumulative10.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZPCumulative20.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZPCumulative30.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZPCumulative40.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZPCumulative50.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZPCumulative60.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZPCumulative70.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZPCumulative80.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.ZPCumulative90.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityTotal.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityMax.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityMean.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityStandardDeviation.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensitySkew.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityKurtosis.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityPGround.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityPCumulativeZQ10.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityPCumulativeZQ30.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityPCumulativeZQ50.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityPCumulativeZQ70.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.IntensityPCumulativeZQ90.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.PFirstReturn.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.PSecondReturn.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.PThirdReturn.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.PFourthReturn.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.PFifthReturn.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.PGround.NoDataValue));
            Assert.IsTrue(Single.IsNaN(abaMetrics.AreaOfPointBoundingBox.NoDataValue));
        }
    }
}