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

            using Dataset gridCellDefinitionDataset = Gdal.Open(Path.Combine(this.unitTestPath, "PSME ABA grid cells.tif"), Access.GA_ReadOnly);
            Raster<UInt16> gridCellDefinitions = new(gridCellDefinitionDataset);
            Grid<PointListZirn> abaGrid = new(gridCellDefinitions);

            Assert.IsTrue(gridCellDefinitions.Crs.IsSame(abaGrid.Crs, Array.Empty<string>()) == 1);
            Assert.IsTrue(RasterGeoTransform.Equals(gridCellDefinitions.Transform, abaGrid.Transform));
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
            Assert.IsTrue(populatedGridCells == 575);

            using FileStream stream = new(Path.Combine(this.unitTestPath, "PSME LAS 1.4 point type 6.las"), FileMode.Open, FileAccess.Read, FileShare.Read, 512 * 1024, FileOptions.SequentialScan);
            using LasReader lasReader = new(stream);
            LasFile lasFile = lasReader.ReadHeader();
            lasReader.ReadVariableLengthRecords(lasFile);

            LasHeader14 lasHeader14 = (LasHeader14)lasFile.Header;
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
            Assert.IsTrue(lasHeader14.StartOfFirstExtendedVariableLengthRecord == (UInt64)stream.Length); // not well defined by spec; proactively setting to end of file is reasonable when there are zero EVLRs
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

            Assert.IsTrue(lasFile.VariableLengthRecords.Count == 1);
            GeoKeyDirectoryTagRecord crs = (GeoKeyDirectoryTagRecord)lasFile.VariableLengthRecords[0];
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

            int lasFileEpsg = lasFile.GetProjectedCoordinateSystemEpsg();
            Assert.IsTrue(lasFileEpsg == 32610);

            lasReader.ReadPointsToGridZirn(lasFile, abaGrid);
            PointListZirn? psmeCell1 = abaGrid[8, 14];
            Assert.IsTrue(psmeCell1 != null);
            Assert.IsTrue(psmeCell1.Z.Count == 162764);
            Assert.IsTrue(psmeCell1.Intensity.Count == psmeCell1.Z.Count);
            Assert.IsTrue(psmeCell1.ReturnNumber.Count == psmeCell1.Z.Count);
            PointListZirn? psmeCell2 = abaGrid[9, 14];
            Assert.IsTrue(psmeCell2 != null);
            Assert.IsTrue(psmeCell2.Z.Count == 44853);
            Assert.IsTrue(psmeCell2.Intensity.Count == psmeCell2.Z.Count);
            Assert.IsTrue(psmeCell2.ReturnNumber.Count == psmeCell2.Z.Count);

            SpatialReference lasSpatialReference = lasFile.GetSpatialReference();
            float crsLinearUnits = (float)lasSpatialReference.GetLinearUnits();
            float oneMeterHeightClass = 1.0F / crsLinearUnits;
            float twoMeterHeightThreshold = 920.52F + 2.0F / crsLinearUnits;
            StandardMetricsRaster abaMetrics = new(abaGrid.Crs, abaGrid.Transform, abaGrid.XSize, abaGrid.YSize);
            psmeCell1.GetStandardMetrics(abaMetrics, oneMeterHeightClass, twoMeterHeightThreshold, 8, 14);
            psmeCell2.GetStandardMetrics(abaMetrics, oneMeterHeightClass, twoMeterHeightThreshold, 9, 14);

            Assert.IsTrue(abaMetrics.N[8, 14] == 162764);
            Assert.IsTrue(abaMetrics.AreaOfPointBoundingBox[8, 14] == 11.5402412F);
            Assert.IsTrue(abaMetrics.ZMax[8, 14] == 928.6306F);
            Assert.IsTrue(abaMetrics.ZMean[8, 14] == 923.698242F);
            Assert.IsTrue(abaMetrics.ZStandardDeviation[8, 14] == 1.69425166F);
            Assert.IsTrue(abaMetrics.ZSkew[8, 14] == 0.3093502F);
            Assert.IsTrue(abaMetrics.ZKurtosis[8, 14] == 2.619736F);
            Assert.IsTrue(abaMetrics.ZNormalizedEntropy[8, 14] == 0.871817946F);
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

            Assert.IsTrue(abaMetrics.AreaOfPointBoundingBox[9, 14] == 3.70189786F);
            Assert.IsTrue(abaMetrics.N[9, 14] == 44853.0F);
            Assert.IsTrue(abaMetrics.ZMax[9, 14] == 926.576538F);
            Assert.IsTrue(abaMetrics.ZMean[9, 14] == 922.74585F);
            Assert.IsTrue(abaMetrics.ZStandardDeviation[9, 14] == 1.39960063F);
            Assert.IsTrue(abaMetrics.ZSkew[9, 14] == 0.329364121F);
            Assert.IsTrue(abaMetrics.ZKurtosis[9, 14] == 2.12710214F);
            Assert.IsTrue(abaMetrics.ZNormalizedEntropy[9, 14] == 0.885059357F);
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
        }
    }
}