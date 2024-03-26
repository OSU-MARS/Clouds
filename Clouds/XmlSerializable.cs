using System;
using System.Globalization;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace Mars.Clouds
{
    public abstract class XmlSerializable : IXmlSerializable
    {
        XmlSchema? IXmlSerializable.GetSchema()
        {
            return null;
        }

        protected static float ReadAttributeAsFloat(XmlReader reader, string attributeName)
        {
            string? attributeValue = reader.GetAttribute(attributeName);
            if (attributeValue == null)
            {
                throw new XmlException("Element " + reader.Name + " does not have a " + attributeName + " attribute.");
            }
            return Single.Parse(attributeValue);
        }
        
        protected static Int32 ReadAttributeAsInt32(XmlReader reader, string attributeName)
        {
            string? attributeValue = reader.GetAttribute(attributeName);
            if (attributeValue == null)
            {
                throw new XmlException("Element " + reader.Name + " does not have a " + attributeName + " attribute.");
            }
            return Int32.Parse(attributeValue);
        }

        protected static UInt32 ReadAttributeAsUInt32Hex(XmlReader reader, string attributeName)
        {
            string? attributeValue = reader.GetAttribute(attributeName);
            if (attributeValue == null)
            {
                throw new XmlException("Element " + reader.Name + " does not have a " + attributeName + " attribute.");
            }
            if (attributeValue.StartsWith("0x") == false)
            {
                throw new XmlException("Element " + reader.Name + "'s " + attributeName + " attribute does not start with '0x'.");
            }
            return UInt32.Parse(attributeValue[2..], NumberStyles.HexNumber);
        }

        public void ReadXml(XmlReader reader)
        {
            if (reader.IsEmptyElement)
            {
                // skip subtree overhead in this case
                // The single call to XmlReader.Read() which ReadStartElement() makes on an empty element doesn't advance the parent reader. The lightest
                // weight option for handling this case is simply not to instantiate a subtree reader.
                this.ReadStartElement(reader);
            }
            else
            {
                using XmlReader elementReader = reader.ReadSubtree();
                elementReader.Read();
                while (elementReader.EOF == false)
                {
                    if (elementReader.IsStartElement())
                    {
                        this.ReadStartElement(elementReader);
                    }
                    else
                    {
                        elementReader.Read();
                    }
                }
            }
        }

        void IXmlSerializable.WriteXml(XmlWriter writer)
        {
            throw new NotSupportedException();
        }

        protected abstract void ReadStartElement(XmlReader reader);
    }
}
