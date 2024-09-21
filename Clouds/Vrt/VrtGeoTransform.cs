using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using System;
using System.Xml;

namespace Mars.Clouds.Vrt
{
    public class VrtGeoTransform : XmlSerializable
    {
        private readonly GridGeoTransform transform;

        public VrtGeoTransform()
        {
            this.transform = new();
        }

        public double CellHeight 
        { 
            get { return this.transform.CellHeight; }
        } 
        
        public double CellWidth
        {
            get { return this.transform.CellWidth; }
        }

        public double ColumnRotation
        {
            get { return this.transform.ColumnRotation; }
        }

        public double OriginX
        {
            get { return this.transform.OriginX; }
        }

        public double OriginY
        {
            get { return this.transform.OriginY; }
        }

        public double RowRotation
        {
            get { return this.transform.RowRotation; }
        }

        public void Copy(GridGeoTransform other)
        {
            this.transform.Copy(other);
        }

        protected override void ReadStartElement(XmlReader reader)
        {
            if (reader.AttributeCount != 0)
            {
                throw new XmlException("Encountered unexpected attributes on element " + reader.Name + ".");
            }

            switch (reader.Name)
            {
                case "GeoTransform":
                    this.transform.Copy(reader.ReadElementContentAsCsvDoubleArray());
                    break;
                default:
                    throw new XmlException("Element '" + reader.Name + "' is unknown, has unexpected attributes, or is missing expected attributes.");
            }
        }

        public void SetCellSize(double width, double height)
        {
            this.transform.SetCellSize(width, height);
        }

        public void SetTransform(ReadOnlySpan<double> padfTransform)
        {
            this.transform.Copy(padfTransform);
        }

        public void WriteXml(XmlWriter writer)
        {
            writer.WriteElementCsv("GeoTransform", this.transform.GetPadfTransform());
        }
    }
}
