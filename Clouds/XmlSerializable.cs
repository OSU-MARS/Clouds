using System;
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
