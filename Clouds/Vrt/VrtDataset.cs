﻿using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml;
using System.Xml.Linq;

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

        public void AppendBands<TTile>(string vrtDatasetDirectory, VirtualRaster<TTile> vrt, List<string> bands) where TTile : Raster
        {
            if ((vrt.TileCount == 0) || (bands.Count == 0))
            {
                // nothing to do
                // Debatable whether requesting bands be appended from a VirtualRaster<T> without any tiles is an error.
                return; 
            }

            // add bands
            for (int bandAddIndex = 0; bandAddIndex < bands.Count; ++bandAddIndex) 
            {
                string bandName = bands[bandAddIndex];
                DataType vrtBandDataType = vrt.BandDataTypes[bandAddIndex];
                VrtRasterBand band = new()
                {
                    Band = this.Bands.Count + 1,
                    DataType = vrtBandDataType,
                    Description = bandName,
                    NoDataValue = RasterBand.GetDefaultNoDataValueAsDouble(vrtBandDataType), // TODO: how to check if band should not have a no data value?
                    ColorInterpretation = VrtRasterBand.GetColorInterpretation(bandName)
                };
                // for now, no metadata or histograms

                // add sources
                for (int tileIndexY = 0; tileIndexY < vrt.VirtualRasterSizeInTilesX; ++tileIndexY)
                {
                    for (int tileIndexX = 0; tileIndexX < vrt.VirtualRasterSizeInTilesX; ++tileIndexX)
                    {
                        TTile? tile = vrt[tileIndexX, tileIndexY];
                        if (tile == null)
                        {
                            continue;
                        }
                        RasterBand tileBand = tile.GetBand(bandName);
                        if (tileBand.HasNoDataValue == false)
                        {
                            throw new NotSupportedException("Band '" + bandName + "' in tile '" + tile.FilePath + "' does not have a no data value.");
                        }
                        int tileBandIndex = tile.GetBandIndex(bandName);
                        if (tileBandIndex == -1)
                        {
                            throw new ArgumentOutOfRangeException(nameof(bands), "Band '" + bands[bandAddIndex] + "' is not present in virtual raster.");
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
                            NoDataValue = tileBand.GetNoDataValueAsDouble()
                        };

                        band.Sources.Add(tileSource);
                    }
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