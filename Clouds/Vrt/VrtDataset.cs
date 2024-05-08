using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml;

namespace Mars.Clouds.Vrt
{
    /// <summary>
    /// https://gdal.org/drivers/raster/vrt.html
    /// </summary>
    public class VrtDataset : XmlSerializable
    {
        // defined in .vrt schema but not currently implemented
        // GCPList { Projection, dataAxisToSRSAxisMapping, GCP }
        // Metadata
        // MaskBand
        // GDALWarpOptions
        // PansharpeningOptions { Algorithm, AlgorithmOptions, Resampling, NumThreads, BitDepth, NoData, SpatialExtentAdjustment, PanchroBand, SpectralBand }
        // Input { SourceFilename, VRTDataset}
        // ProcessingSteps { Step }
        // Group
        // OverviewList

        public UInt32 BlockXSize { get; private set; }
        public UInt32 BlockYSize { get; private set; }
        public UInt32 RasterXSize { get; set; }
        public UInt32 RasterYSize { get; set; }
        public VrtDatasetSubclass Subclass { get; private set; }

        public SpatialReferenceSystem Srs { get; private init; }
        public VrtGeoTransform GeoTransform { get; private init; }
        public VrtMetadata Metadata { get; private init; }
        public List<VrtRasterBand> Bands { get; private init; }

        public VrtDataset()
        {
            this.BlockXSize = UInt32.MaxValue;
            this.BlockYSize = UInt32.MaxValue;
            this.RasterXSize = UInt32.MaxValue;
            this.RasterYSize = UInt32.MaxValue;
            this.Subclass = VrtDatasetSubclass.None;

            this.Srs = new();
            this.GeoTransform = new();
            this.Metadata = new();
            this.Bands = [];
        }

        public VrtDataset(string vrtDatasetPath)
            : this()
        {
            using FileStream stream = new(vrtDatasetPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using XmlReader reader = XmlReader.Create(stream);
            reader.MoveToContent();
            this.ReadXml(reader);
        }

        public void AppendBands<TTile>(string vrtDatasetDirectory, VirtualRaster<TTile> vrt, List<string> bands, GridNullable<RasterBandStatistics[]>? statisticsByTile) where TTile : Raster
        {
            if ((vrt.TileCount == 0) || (bands.Count == 0))
            {
                // nothing to do
                // Debatable whether requesting bands be appended from a VirtualRaster<T> without any tiles is an error.
                return; 
            }

            // add bands
            for (int bandIndex = 0; bandIndex < bands.Count; ++bandIndex) 
            {
                string bandName = bands[bandIndex];
                int tilesWithNoDataValue = vrt.TilesWithNoDataValuesByBand[bandIndex];
                if ((tilesWithNoDataValue != 0) && (tilesWithNoDataValue != vrt.TileCount))
                {
                    throw new ArgumentOutOfRangeException(nameof(vrt), "No data values are inconsistently present in virtual raster band '" + bandName + "'. " + tilesWithNoDataValue + " of the virtual raster's " + vrt.TileCount + " tiles have no data values ('" + vrtDatasetDirectory + "')");
                }

                // create band
                DataType bandDataType = vrt.BandDataTypes[bandIndex];
                VrtRasterBand band = new()
                {
                    Band = this.Bands.Count + 1,
                    DataType = bandDataType,
                    Description = bandName,
                    ColorInterpretation = VrtRasterBand.GetColorInterpretation(bandName)
                };
                if (tilesWithNoDataValue > 0)
                {
                    // set no data value for virtual raster's band
                    // This can differ from the tiles' no data values and, in cases where band type varies, often does differ from tile values.
                    Debug.Assert(vrt.NoDataValuesByBand[bandIndex].Count > 0);
                    band.NoDataValue = VrtDataset.ResolveBandNoDataValue(bandName, bandDataType, vrt.NoDataValuesByBand[bandIndex]);
                }

                // add sources and combine statistics from sampled tiles
                RasterBandStatistics bandStatistics = new();
                int tilesWithStatistics = 0;
                for (int tileIndexY = 0; tileIndexY < vrt.VirtualRasterSizeInTilesY; ++tileIndexY)
                {
                    for (int tileIndexX = 0; tileIndexX < vrt.VirtualRasterSizeInTilesX; ++tileIndexX)
                    {
                        TTile? tile = vrt[tileIndexX, tileIndexY];
                        if (tile == null)
                        {
                            Debug.Assert((statisticsByTile == null) || (statisticsByTile[tileIndexX, tileIndexY] == null));
                            continue;
                        }

                        // sources
                        RasterBand tileBand = tile.GetBand(bandName);
                        int tileBandIndex = tile.GetBandIndex(bandName);
                        if (tileBandIndex == -1)
                        {
                            throw new ArgumentOutOfRangeException(nameof(bands), "Band '" + bands[bandIndex] + "' is not present in virtual raster.");
                        }

                        string relativePathToTile = Path.GetRelativePath(vrtDatasetDirectory, tile.FilePath);
                        if (Path.DirectorySeparatorChar == '\\')
                        {
                            relativePathToTile = relativePathToTile.Replace(Path.DirectorySeparatorChar, '/');
                        }
                        ComplexSource tileSource = new()
                        {
                            SourceFilename = { RelativeToVrt = true, Filename = relativePathToTile },
                            SourceBand = tileBandIndex + 1,
                            SourceProperties = { RasterXSize = (UInt32)tile.SizeX, RasterYSize = (UInt32)tile.SizeY, DataType = tileBand.GetGdalDataType() },
                            SourceRectangle = { XOffset = 0.0, YOffset = 0.0, XSize = tile.SizeX, YSize = tile.SizeY },
                            DestinationRectangle = { XOffset = tileIndexX * vrt.TileSizeInCellsX, YOffset = tileIndexY * vrt.TileSizeInCellsY, XSize = tile.SizeX,YSize = tile.SizeY },
                        };
                        if (tileBand.HasNoDataValue)
                        {
                            tileSource.NoDataValue = tileBand.GetNoDataValueAsDouble();
                        }

                        band.Sources.Add(tileSource);

                        // statistics
                        if (statisticsByTile != null)
                        {
                            RasterBandStatistics[]? statisticsForTile = statisticsByTile[tileIndexX, tileIndexY];
                            if (statisticsForTile != null)
                            {
                                if (statisticsForTile.Length != bands.Count)
                                {
                                    throw new ArgumentOutOfRangeException(nameof(statisticsByTile), "Statistics for " + statisticsForTile.Length + " bands were provided for tile at (" + tileIndexX + ", " + tileIndexY + ") instead of the expected " + bands.Count + " bands.");
                                }

                                bandStatistics.Add(statisticsForTile[bandIndex]);
                                ++tilesWithStatistics;
                            }
                        }
                    }
                }

                if (statisticsByTile != null)
                {
                    Debug.Assert(tilesWithStatistics <= vrt.TileCount);
                    bandStatistics.IsApproximate = tilesWithStatistics < vrt.TileCount;
                    bandStatistics.OnAdditionComplete();
                    Debug.Assert(bandStatistics.CellsSampled > 0);

                    // add band statistics to metadata
                    band.Metadata.Add(bandStatistics);

                    // define crude histogram from band statistics
                    band.Histograms.Add(new(bandStatistics));
                }

                this.Bands.Add(band);
            }
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            switch (reader.Name)
            {
                case "VRTDataset":
                    if ((reader.AttributeCount < 2) || (reader.AttributeCount > 4))
                    {
                        throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
                    }
                    this.RasterXSize = reader.ReadAttributeAsUInt32("rasterXSize");
                    this.RasterYSize = reader.ReadAttributeAsUInt32("rasterYSize");
                    if (reader.AttributeCount == 3)
                    {
                        this.Subclass = reader.ReadAttributeAsVrtDatasetSubclass("subClass");
                    }
                    reader.Read();
                    break;
                case "SRS":
                    this.Srs.ReadXml(reader);
                    break;
                case "GeoTransform":
                    this.GeoTransform.ReadXml(reader);
                    break;
                case "BlockXSize":
                    if (reader.AttributeCount != 0)
                    {
                        throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
                    }
                    this.BlockXSize = reader.ReadElementContentAsUInt32();
                    break;
                case "BlockYSize":
                    if (reader.AttributeCount != 0)
                    {
                        throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
                    }
                    this.BlockXSize = reader.ReadElementContentAsUInt32();
                    break;
                case "Metadata":
                    if (reader.AttributeCount != 0)
                    {
                        throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
                    }
                    this.Metadata.ReadXml(reader);
                    break;
                case "VRTRasterBand":
                    VrtRasterBand band = new();
                    band.ReadXml(reader);
                    this.Bands.Add(band);
                    break;
                //case "MaskBand":
                //case "GDALWarpOptions":
                //case "PansharpeningOptions":
                //case "Input":
                //case "ProcessingSteps":
                //case "Group":
                default:
                    throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }

        private static double ResolveBandNoDataValue(string bandName, DataType gdalDataType, List<double> noDataValues)
        {
            if (noDataValues.Count == 0)
            {
                return RasterBand.GetDefaultNoDataValueAsDouble(gdalDataType);
            }
            else if (noDataValues.Count == 1)
            {
                return noDataValues[0];
            }

            return gdalDataType switch
            {
                DataType.GDT_Byte => RasterBand.ResolveUnsignedIntegerNoDataValue(noDataValues, Byte.MaxValue),
                DataType.GDT_Int8 => RasterBand.ResolveSignedIntegerNoDataValue(noDataValues, SByte.MinValue, SByte.MaxValue),
                DataType.GDT_Int16 => RasterBand.ResolveSignedIntegerNoDataValue(noDataValues, Int16.MinValue, Int16.MaxValue),
                DataType.GDT_Int32 => RasterBand.ResolveSignedIntegerNoDataValue(noDataValues, Int32.MinValue, Int32.MaxValue),
                DataType.GDT_Int64 => RasterBand.ResolveSignedIntegerNoDataValue(noDataValues, Int64.MinValue, Int64.MaxValue),
                DataType.GDT_UInt16 => RasterBand.ResolveUnsignedIntegerNoDataValue(noDataValues, UInt16.MaxValue),
                DataType.GDT_UInt32 => RasterBand.ResolveUnsignedIntegerNoDataValue(noDataValues, UInt32.MaxValue),
                DataType.GDT_UInt64 => RasterBand.ResolveUnsignedIntegerNoDataValue(noDataValues, UInt64.MaxValue),
                DataType.GDT_Float32 or
                DataType.GDT_Float64 => throw new NotSupportedException("Multiple " + gdalDataType + " no data values found in virtual raster tiles for band '" + bandName + "'. Making a selection among these (or picking an alternate value) is not currently implemented."),
                _ => throw new NotSupportedException("Unhandled GDAL data type " + gdalDataType + ".")
            };
        }

        public void WriteXml(string vrtFilePath)
        {
            // GDAL 3.8.5 (and QGIS 3.34) don't quite follow XML standard when writing .vrt files
            // Standard XML is interoperable with GDAL but the settings here are used to produce .vrts which look like GDAL's.
            XmlWriterSettings vrtSettings = new()
            {
                OmitXmlDeclaration = true,
                Indent = true
                // GDAL uses IndentChars default of two spaces
            };
            using XmlWriter writer = XmlWriter.Create(vrtFilePath, vrtSettings);
            this.WriteXml(writer);
        }

        public void WriteXml(XmlWriter writer)
        {
            writer.WriteStartElement("VRTDataset");
            writer.WriteAttributeDoubleOrNan("rasterXSize", this.RasterXSize);
            writer.WriteAttributeDoubleOrNan("rasterYSize", this.RasterYSize);
            if (this.Subclass != VrtDatasetSubclass.None)
            {
                writer.WriteAttribute("subClass", this.Subclass);
            }
            this.Srs.WriteXml(writer);
            this.GeoTransform.WriteXml(writer);

            if (this.BlockXSize != UInt32.MaxValue)
            {
                writer.WriteElementString("BlockXSize", this.BlockXSize);
            }
            if (this.BlockYSize != UInt32.MaxValue)
            {
                writer.WriteElementString("BlockYSize", this.BlockYSize);
            }

            for (int bandIndex = 0; bandIndex < this.Bands.Count; ++bandIndex)
            {
                this.Bands[bandIndex].WriteXml(writer);
            }
            writer.WriteEndElement();
        }
    }
}
