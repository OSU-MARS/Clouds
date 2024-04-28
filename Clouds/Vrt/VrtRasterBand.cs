using Mars.Clouds.Extensions;
using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.Xml;

namespace Mars.Clouds.Vrt
{
    public class VrtRasterBand : XmlSerializable
    {
        // not implemented: @subClass: VrtRasterBandSubClass
        //   UnitType, Scale, Offset, CategoryNames, ColorTable, HideNoDataValue, Overview, MaskBand
        //   sourced raster band: SimpleSource, AveragedSource, NoDataFromMaskSource, KernelFilteredSource, ArraySource
        //   derived raster band: PixelFunctionType, SourceTransferType, PixelFunctionLanguage, PixelFunctionCode, PixelFunctionArguments, BufferRadius, SkipNonContributingSources
        //   raw raster band: SourceFilename, ImageOffset, PixelOffset, LineOffset, ByteOrder
        public int Band { get; set; }
        public ColorInterpretation ColorInterpretation { get; set; }
        public DataType DataType { get; set; }
        public string Description { get; set; }
        public List<HistogramItem> Histograms { get; private init; }
        public VrtMetadata Metadata { get; private init; }
        public double NoDataValue { get; set; }
        public List<ComplexSource> Sources { get; private init; }

        public VrtRasterBand()
        {
            this.Band = -1;
            this.ColorInterpretation = ColorInterpretation.Unknown;
            this.DataType = DataType.GDT_Unknown;
            this.Description = String.Empty;
            this.Histograms = [];
            this.Metadata = new();
            this.NoDataValue = Double.NaN;
            this.Sources = [];
        }

        public static ColorInterpretation GetColorInterpretation(string name)
        {
            if (Enum.TryParse<ColorInterpretation>(name, ignoreCase: true, out ColorInterpretation colorInterpretation))
            {
                return colorInterpretation;
            }

            return ColorInterpretation.Gray; // absent any other general default
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount == 0)
            {
                switch (reader.Name)
                {
                    case "Description":
                        this.Description = reader.ReadElementContentAsString();
                        break;
                    case "HistItem":
                        HistogramItem histogram = new();
                        histogram.ReadXml(reader);
                        this.Histograms.Add(histogram);
                        break;
                    case "Histograms":
                        reader.Read();
                        break;
                    case "Metadata":
                        this.Metadata.ReadXml(reader);
                        break;
                    // case "NodataValue": // for backwards compatibility per .vrt schema if needed
                    case "NoDataValue":
                        this.NoDataValue = reader.ReadElementContentAsDouble();
                        break;
                    case "ColorInterp":
                        this.ColorInterpretation = reader.ReadElementContentAsEnum<ColorInterpretation>();
                        break;
                    case "ComplexSource":
                        ComplexSource source = new();
                        source.ReadXml(reader);
                        this.Sources.Add(source);
                        break;
                    default:
                        throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
                }
            }
            else if (reader.AttributeCount == 2)
            {
                switch (reader.Name)
                {
                    case "VRTRasterBand":
                        this.Band = reader.ReadAttributeAsInt32("band");
                        this.DataType = reader.ReadAttributeAsGdalDataType("dataType");
                        reader.Read();
                        break;
                    default:
                        throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
                }
            }
            else
            {
                throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }

        public void WriteXml(XmlWriter writer)
        {
            writer.WriteStartElement("VRTRasterBand");
            writer.WriteAttribute("dataType", this.DataType);
            writer.WriteAttributeDoubleOrNan("band", this.Band);
            writer.WriteElementString("Description", this.Description);
            writer.WriteElementDoubleOrNaN("NoDataValue", this.NoDataValue);
            this.Metadata.WriteXml(writer);
            writer.WriteElementString("ColorInterp", this.ColorInterpretation);
            if (this.Histograms.Count > 0)
            {
                writer.WriteStartElement("Histograms");
                for (int histogramIndex = 0; histogramIndex < this.Histograms.Count; ++histogramIndex)
                {
                    this.Histograms[histogramIndex].WriteXml(writer);
                }
                writer.WriteEndElement();
            }

            for (int sourceIndex = 0; sourceIndex < this.Sources.Count; ++sourceIndex)
            {
                this.Sources[sourceIndex].WriteXml(writer);
            }
            writer.WriteEndElement();
        }
    }
}
