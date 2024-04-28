using Mars.Clouds.Vrt;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OSGeo.GDAL;
using System;
using System.IO;
using System.Xml;

namespace Mars.Clouds.UnitTests
{
    [TestClass]
    public class VrtTests : CloudTest
    {
        [TestMethod]
        public void Read()
        {
            VrtExpectation dsmExpectation = new()
            {
                BandCount = 6,
                OriginX = 345000.0,
                OriginY = 732000.0,
                RasterXSize = 60000,
                RasterYSize = 64000,
                BandType = [ DataType.GDT_Float32, DataType.GDT_Float32, DataType.GDT_Float32, DataType.GDT_UInt32, DataType.GDT_UInt32, DataType.GDT_UInt16 ],
                SourceBandIndex = [ 1, 2, 3, 1, 2, 10 ],
                ColorInterpretation = [ ColorInterpretation.Gray, ColorInterpretation.Unknown, ColorInterpretation.Unknown, ColorInterpretation.Unknown, ColorInterpretation.Unknown, ColorInterpretation.Unknown ],
                NoDataValue = [ Double.NaN, Double.NaN, Double.NaN, UInt32.MaxValue, UInt32.MaxValue, UInt16.MaxValue ],
                Histograms = [ 0, 0, 0, 0, 0, 0 ],
                Metadata = [ 0, 0, 0, 0, 0, 0 ],
                Sources = 561
            };
            VrtExpectation orthoimageExpectation = new()
            {
                BandCount = 3,
                OriginX = 348000.0,
                OriginY = 729000.0,
                RasterXSize = 56000,
                RasterYSize = 62000,
                BandType = [ DataType.GDT_UInt16, DataType.GDT_UInt16, DataType.GDT_UInt16 ],
                SourceBandIndex = [1, 2, 3],
                ColorInterpretation = [ ColorInterpretation.Red, ColorInterpretation.Green, ColorInterpretation.Blue ],
                NoDataValue = [ UInt16.MaxValue, UInt16.MaxValue, UInt16.MaxValue ],
                Histograms = [ 1, 1, 1 ],
                Metadata = [ 6, 6, 6 ],
                Sources = 510
            };

            VrtDataset dsm = new(Path.Combine(this.UnitTestPath, "Elliott 2021 DSM subset.vrt"));
            VrtDataset orthoimage = new(Path.Combine(this.UnitTestPath, "Elliott 2021 orthoimage.vrt"));

            VrtTests.VerifyVrt(dsmExpectation, dsm);
            VrtTests.VerifyVrt(orthoimageExpectation, orthoimage);
        }

        [TestMethod]
        public void Roundtrip()
        {
            string epsg6557 = "PROJCS[\"NAD83(2011) / Oregon GIC Lambert (ft)\",GEOGCS[\"NAD83(2011)\",DATUM[\"NAD83_National_Spatial_Reference_System_2011\",SPHEROID[\"GRS 1980\",6378137,298.257222101,AUTHORITY[\"EPSG\",\"7019\"]],AUTHORITY[\"EPSG\",\"1116\"]],PRIMEM[\"Greenwich\",0,AUTHORITY[\"EPSG\",\"8901\"]],UNIT[\"degree\",0.0174532925199433,AUTHORITY[\"EPSG\",\"9122\"]],AUTHORITY[\"EPSG\",\"6318\"]],PROJECTION[\"Lambert_Conformal_Conic_2SP\"],PARAMETER[\"latitude_of_origin\",41.75],PARAMETER[\"central_meridian\",-120.5],PARAMETER[\"standard_parallel_1\",43],PARAMETER[\"standard_parallel_2\",45.5],PARAMETER[\"false_easting\",1312335.958],PARAMETER[\"false_northing\",0],UNIT[\"foot\",0.3048,AUTHORITY[\"EPSG\",\"9002\"]],AXIS[\"Easting\",EAST],AXIS[\"Northing\",NORTH],AUTHORITY[\"EPSG\",\"6557\"]]";

            VrtDataset minimalVrt = new()
            {
                RasterXSize = 1 * 2000,
                RasterYSize = 1 * 2000
            };
            minimalVrt.Srs.DataAxisToSrsAxisMapping = [ 1, 2, 3 ];
            minimalVrt.Srs.WktGeogcsOrProj = epsg6557;
            minimalVrt.GeoTransform.SetTransform([ 345000.0, 1.5, 0.0, 732000.0, 0.0, -1.5 ]);

            VrtRasterBand band1 = new()
            {
                DataType = DataType.GDT_Float32,
                Band = 1,
                Description = "dsm",
                // leave NoDataValue as Double.NaN,
                ColorInterpretation = ColorInterpretation.Gray
            };
            ComplexSource source1 = new()
            {
                SourceBand = 1,
                SourceFilename = { RelativeToVrt = true, Filename = "s04320w06990.tif" },
                SourceProperties = { RasterXSize = 2000, RasterYSize = 2000, DataType = DataType.GDT_Float32, BlockXSize = 2000, BlockYSize = 1},
                SourceRectangle = { XOffset = 0.0, YOffset = 0.0, XSize = 2000.0, YSize = 2000.0 },
                DestinationRectangle = { XOffset = 58000.0, YOffset = 20000.0, XSize = 2000.0, YSize = 2000.0 }
                // leave NoDataValue as Double.NaN
            };
            band1.Sources.Add(source1);
            minimalVrt.Bands.Add(band1);

            using MemoryStream stream = new();
            using XmlWriter writer = XmlWriter.Create(stream);
            minimalVrt.WriteXml(writer);
            writer.Flush();

            // useful for debugging
            //stream.TryGetBuffer(out ArraySegment<byte> buffer);
            //string xml = Encoding.UTF8.GetString(buffer);

            stream.Seek(0, SeekOrigin.Begin);
            VrtDataset copyOfMinimumVrt = new();
            using XmlReader reader = XmlReader.Create(stream);
            reader.MoveToContent();
            copyOfMinimumVrt.ReadXml(reader);

            Assert.IsTrue((copyOfMinimumVrt.BlockXSize == UInt32.MaxValue) && (copyOfMinimumVrt.BlockYSize == UInt32.MaxValue) && (copyOfMinimumVrt.RasterXSize == 2000) && (copyOfMinimumVrt.RasterYSize == 2000));
            Assert.IsTrue((copyOfMinimumVrt.Srs.DataAxisToSrsAxisMapping[0] == 1) && (copyOfMinimumVrt.Srs.DataAxisToSrsAxisMapping[1] == 2) && (copyOfMinimumVrt.Srs.DataAxisToSrsAxisMapping[2] == 3) && String.Equals(copyOfMinimumVrt.Srs.WktGeogcsOrProj, epsg6557, StringComparison.Ordinal));
            Assert.IsTrue((copyOfMinimumVrt.GeoTransform.OriginX == 345000.0) && (copyOfMinimumVrt.GeoTransform.CellWidth == 1.5) && (copyOfMinimumVrt.GeoTransform.ColumnRotation == 0.0) && (copyOfMinimumVrt.GeoTransform.OriginY == 732000.0) && (copyOfMinimumVrt.GeoTransform.CellHeight == -1.5) && (copyOfMinimumVrt.GeoTransform.RowRotation == 0.0));
            Assert.IsTrue(copyOfMinimumVrt.Metadata.Count == 0);
            Assert.IsTrue(copyOfMinimumVrt.Bands.Count == 1);

            VrtRasterBand band1copy = copyOfMinimumVrt.Bands[0];
            Assert.IsTrue((band1.DataType == band1copy.DataType) && (band1.Band == band1copy.Band) && String.Equals(band1.Description, band1copy.Description, StringComparison.Ordinal) && Double.IsNaN(band1copy.NoDataValue) && (band1.ColorInterpretation == band1copy.ColorInterpretation) && (band1copy.Sources.Count == 1));
            ComplexSource source1copy = band1copy.Sources[0];
            Assert.IsTrue((source1.SourceFilename.RelativeToVrt == source1copy.SourceFilename.RelativeToVrt) && String.Equals(source1.SourceFilename.Filename, source1copy.SourceFilename.Filename, StringComparison.Ordinal) && (source1.SourceBand == source1copy.SourceBand));
            Assert.IsTrue((source1.SourceRectangle.XOffset == source1copy.SourceRectangle.XOffset) && (source1.SourceRectangle.YOffset == source1copy.SourceRectangle.YOffset) && (source1.SourceRectangle.XSize == source1copy.SourceRectangle.XSize) && (source1.SourceRectangle.YSize == source1copy.SourceRectangle.YSize));
            Assert.IsTrue((source1.DestinationRectangle.XOffset == source1copy.DestinationRectangle.XOffset) && (source1.DestinationRectangle.YOffset == source1copy.DestinationRectangle.YOffset) && (source1.DestinationRectangle.XSize == source1copy.DestinationRectangle.XSize) && (source1.DestinationRectangle.YSize == source1copy.DestinationRectangle.YSize));
        }

        private static void VerifyVrt(VrtExpectation expected, VrtDataset actual)
        {
            Assert.IsTrue(actual.Bands.Count == expected.BandCount);
            Assert.IsTrue((actual.RasterXSize == expected.RasterXSize) && (actual.RasterYSize == expected.RasterYSize));
            Assert.IsTrue((actual.Srs.DataAxisToSrsAxisMapping.Length == 3) && (actual.Srs.DataAxisToSrsAxisMapping[0] == 1) && (actual.Srs.DataAxisToSrsAxisMapping[1] == 2) && (actual.Srs.DataAxisToSrsAxisMapping[2] == 3));
            Assert.IsTrue((actual.Srs.WktGeogcsOrProj.Length > 1000) && (actual.Srs.WktGeogcsOrProj.Length < 1024));
            Assert.IsTrue((actual.GeoTransform.CellHeight == -1.5) && (actual.GeoTransform.CellWidth == 1.5) && (actual.GeoTransform.ColumnRotation == 0.0) && (actual.GeoTransform.OriginX == expected.OriginX) && (actual.GeoTransform.OriginY == expected.OriginY) && (actual.GeoTransform.RowRotation == 0.0));
            for (int bandIndex = 0; bandIndex < actual.Bands.Count; ++bandIndex)
            {
                VrtRasterBand band = actual.Bands[bandIndex];
                Assert.IsTrue(band.Band == bandIndex + 1);
                Assert.IsTrue(band.ColorInterpretation == expected.ColorInterpretation[bandIndex]);
                Assert.IsTrue(band.DataType == expected.BandType[bandIndex]);
                Assert.IsTrue((band.Description.Length > 2) && (band.Description.Length < 16));
                Assert.IsTrue(band.Histograms.Count == expected.Histograms[bandIndex]);
                Assert.IsTrue(band.Metadata.Count == expected.Metadata[bandIndex]);

                double expectedBandNoData = expected.NoDataValue[bandIndex];
                bool expectedBandNoDataIsNaN = Double.IsNaN(expectedBandNoData);
                if (expectedBandNoDataIsNaN)
                {
                    Assert.IsTrue(Double.IsNaN(band.NoDataValue));
                }
                else
                {
                    Assert.IsTrue(expectedBandNoData == band.NoDataValue);
                }

                Assert.IsTrue(band.Sources.Count == expected.Sources);
                for (int sourceIndex = 0; sourceIndex < band.Sources.Count; ++sourceIndex)
                {
                    ComplexSource source = band.Sources[sourceIndex];
                    Assert.IsTrue(source.SourceFilename.RelativeToVrt && source.SourceFilename.Filename.EndsWith(".tif"));
                    Assert.IsTrue(source.SourceBand == expected.SourceBandIndex[bandIndex]);
                    Assert.IsTrue((source.SourceProperties.RasterXSize == 2000) && (source.SourceProperties.RasterYSize == 2000) && (source.SourceProperties.DataType == expected.BandType[bandIndex]) && (source.SourceProperties.BlockXSize == 2000) && (source.SourceProperties.BlockYSize == 1));
                    Assert.IsTrue((source.DestinationRectangle.XOffset >= 0.0) && (source.DestinationRectangle.XOffset < actual.RasterXSize) && (source.DestinationRectangle.YOffset >= 0.0) && (source.DestinationRectangle.YOffset < actual.RasterYSize) && (source.DestinationRectangle.XSize == 2000.0) && (source.DestinationRectangle.YSize == 2000.0));
                    Assert.IsTrue((source.SourceRectangle.XOffset == 0.0) && (source.SourceRectangle.YOffset == 0.0) && (source.SourceRectangle.XSize == 2000.0) && (source.SourceRectangle.YSize == 2000.0));
                    if (expectedBandNoDataIsNaN)
                    {
                        Assert.IsTrue(Double.IsNaN(source.NoDataValue));
                    }
                    else
                    {
                        Assert.IsTrue(expectedBandNoData == source.NoDataValue);
                    }
                }
            }
        }
    }
}
