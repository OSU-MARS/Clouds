using Mars.Clouds.Extensions;
using System;
using System.Xml;

namespace Mars.Clouds.Vrt
{
    public class SpatialReferenceSystem : XmlSerializable
    {
        // not implemented: @coordinateEpoch
        public int[] DataAxisToSrsAxisMapping { get; set; }
        public string WktGeogcsOrProj { get; set; }

        public SpatialReferenceSystem()
        {
            this.DataAxisToSrsAxisMapping = [];
            this.WktGeogcsOrProj = String.Empty;
        }

        //   <SRS dataAxisToSRSAxisMapping = "1,2" > PROJCS["NAD83(2011) / Oregon GIC Lambert (ft)", GEOGCS["NAD83(2011)", DATUM["NAD83_National_Spatial_Reference_System_2011", SPHEROID["GRS 1980", 6378137, 298.257222101, AUTHORITY["EPSG", "7019"]], AUTHORITY["EPSG", "1116"]], PRIMEM["Greenwich", 0, AUTHORITY["EPSG", "8901"]], UNIT["degree", 0.0174532925199433, AUTHORITY["EPSG", "9122"]], AUTHORITY["EPSG", "6318"]], PROJECTION["Lambert_Conformal_Conic_2SP"], PARAMETER["latitude_of_origin", 41.75], PARAMETER["central_meridian", -120.5], PARAMETER["standard_parallel_1", 43], PARAMETER["standard_parallel_2", 45.5], PARAMETER["false_easting", 1312335.958], PARAMETER["false_northing", 0], UNIT["foot", 0.3048, AUTHORITY["EPSG", "9002"]], AXIS["Easting", EAST], AXIS["Northing", NORTH], AUTHORITY["EPSG", "6557"]] </ SRS >
        protected override void ReadStartElement(XmlReader reader)
        {
            switch (reader.Name)
            {
                case "SRS":
                    if (reader.AttributeCount != 1)
                    {
                        throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
                    }
                    this.DataAxisToSrsAxisMapping = reader.ReadAttributeAsCsvIntegerArray("dataAxisToSRSAxisMapping");
                    this.WktGeogcsOrProj = reader.ReadElementContentAsString();
                    break;
                default:
                    throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }

        public void WriteXml(XmlWriter writer)
        {
            writer.WriteStartElement("SRS");
            writer.WriteAttributeCsv("dataAxisToSRSAxisMapping", this.DataAxisToSrsAxisMapping);
            writer.WriteString(this.WktGeogcsOrProj);
            writer.WriteEndElement();
        }
    }
}