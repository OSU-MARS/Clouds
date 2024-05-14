using DocumentFormat.OpenXml.Spreadsheet;
using Mars.Clouds.GdalExtensions;
using System;

namespace Mars.Clouds
{
    public class XlsxSerializable
    {
        protected static Cell CreateCell(string cellReference, long value)
        {
            if (value < Int32.MaxValue)
            {
                return new Cell() { CellReference = cellReference, CellValue = new((int)value), DataType = CellValues.Number };
            }

            return new Cell() { CellReference = cellReference, CellValue = new((double)value), DataType = CellValues.Number };
        }

        protected static Row CreateHeaderRow(params string[] cellValues)
        {
            if (cellValues.Length > 26)
            {
                throw new ArgumentOutOfRangeException(nameof(cellValues), "Column references beyond Z are not currently supported.");
            }

            Row row = new() { RowIndex = 1 };
            char columnLetter = 'A';
            for (int columnIndex = 0; columnIndex < cellValues.Length; ++columnIndex, ++columnLetter)
            {
                row.Append(new Cell() { CellReference = String.Concat(columnLetter, row.RowIndex), CellValue = new(cellValues[columnIndex]), DataType = CellValues.String });
            }
            return row;
        }
    }
}
