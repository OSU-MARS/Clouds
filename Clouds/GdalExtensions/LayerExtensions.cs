using OSGeo.OGR;
using System;

namespace Mars.Clouds.GdalExtensions
{
    internal static class LayerExtensions
    {
        public static void CreateField(this Layer layer, FieldDefn fieldDefinition)
        {
            int fieldCreationResult = layer.CreateField(fieldDefinition, approx_ok: 0);
            if (fieldCreationResult != OgrError.NONE)
            {
                throw new InvalidOperationException("Failed to create field '" + fieldDefinition.GetName() + "' (OGR error code " + fieldCreationResult + ").");
            }
        }

        public static void CreateField(this Layer layer, string name, FieldType type)
        {
            layer.CreateField(new FieldDefn(name, type));
        }
    }
}
