using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml;
using System.Collections.Generic;
using System.IO;
using System;

namespace Mars.Clouds.GdalExtensions
{
    public class TileStatisticsTable
    {
        private readonly List<string> bandNames;
        private readonly List<RasterBandStatistics> bandStatistics;
        private readonly List<string> sharedStringBandNames;
        private readonly List<string> tileNames;

        public TileStatisticsTable()
        {
            this.bandNames = [];
            this.bandStatistics = [];
            this.sharedStringBandNames = [];
            this.tileNames = [];
        }

        public void Add(string tileName, string bandName, RasterBandStatistics bandStatistics)
        {
            // could flatten values directly to a .xlsx row
            this.bandNames.Add(bandName);
            this.bandStatistics.Add(bandStatistics);
            this.tileNames.Add(tileName);
        }

        private static Cell CreateCell(string cellReference, long value)
        {
            if (value < Int32.MaxValue)
            {
                return new Cell() { CellReference = cellReference, CellValue = new((int)value), DataType = CellValues.Number };
            }

            return new Cell() { CellReference = cellReference, CellValue = new((double)value), DataType = CellValues.Number };
        }

        private static Row CreateDataRow(int rowIndex, string tileName, string bandName, RasterBandStatistics bandStatistics)
        {
            // setting a cell's value does not set its data type
            // Using shared strings for tile names would lead to a smaller .xlsx if tiles have several bands. Shared strings for
            // short band names also result in size increase over fixed strings. For now, just use unshared strings.
            Row row = new() { RowIndex = (UInt32)rowIndex };
            row.Append(new Cell() { CellReference = "A" + row.RowIndex, CellValue = new(tileName), DataType = CellValues.String });
            //if (bandName.Length > 5) // length threshold for worthwile savings TBD, definitely greater than 4
            //{
            //    int bandSharedStringIndex = this.sharedStringBandNames.IndexOf(bandName);
            //    if (bandSharedStringIndex == -1)
            //    {
            //        bandSharedStringIndex = this.sharedStringBandNames.Count;
            //        this.sharedStringBandNames.Add(bandName);
            //    }
            //
            //    row.Append(new Cell() { CellReference = "B" + row.RowIndex, CellValue = new(bandSharedStringIndex), DataType = CellValues.SharedString });
            //}
            //else
            //{
            row.Append(new Cell() { CellReference = "B" + row.RowIndex, CellValue = new(bandName), DataType = CellValues.String });
            //}

            row.Append(TileStatisticsTable.CreateCell("C" + row.RowIndex, bandStatistics.CellsSampled));
            row.Append(TileStatisticsTable.CreateCell("D" + row.RowIndex, bandStatistics.NoDataCells));
            row.Append(new Cell() { CellReference = "E" + row.RowIndex, CellValue = new(bandStatistics.Minimum), DataType = CellValues.Number });
            row.Append(new Cell() { CellReference = "F" + row.RowIndex, CellValue = new(bandStatistics.Mean), DataType = CellValues.Number });
            row.Append(new Cell() { CellReference = "G" + row.RowIndex, CellValue = new(bandStatistics.Maximum), DataType = CellValues.Number });
            row.Append(new Cell() { CellReference = "H" + row.RowIndex, CellValue = new(bandStatistics.StandardDeviation), DataType = CellValues.Number });

            return row;
        }

        private static Row CreateHeaderRow(params string[] cellValues)
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

        public void Write(Stream stream)
        {
            using SpreadsheetDocument spreadsheetDocument = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook);
            WorkbookPart workbookPart = spreadsheetDocument.AddWorkbookPart();
            workbookPart.Workbook = new();
            WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            SheetData bandStatisticsData = new();
            worksheetPart.Worksheet = new(bandStatisticsData);

            Sheets sheets = workbookPart.Workbook.AppendChild(new Sheets());
            Sheet bandStatisticsSheet = new() 
            {   
                Id = workbookPart.GetIdOfPart(worksheetPart),
                Name = "band statistics",
                SheetId = 1
            };
            sheets.Append(bandStatisticsSheet);

            // header row
            Row headerRow = TileStatisticsTable.CreateHeaderRow("tile", "band", "cells", "noData", "minimum", "mean", "maximum", "standardDeviation");
            bandStatisticsData.Append(headerRow);

            // data rows
            for (int index = 0; index < this.bandStatistics.Count; ++index) 
            {
                string tileName = this.tileNames[index];
                string bandName = this.bandNames[index];
                RasterBandStatistics bandStatistics = this.bandStatistics[index];

                // row index: +1 for header row, +1 for OpenXml cell indexing being ones based
                // band name index in shared string table: zero based
                bandStatisticsData.Append(TileStatisticsTable.CreateDataRow(index + 2, tileName, bandName, bandStatistics));
            }

            workbookPart.Workbook.Save();

            // shared string table
            //if (this.uniqueBandNames.Count > 0)
            //{
            //    SharedStringTablePart sharedStringTablePart = workbookPart.AddNewPart<SharedStringTablePart>();
            //    sharedStringTablePart.SharedStringTable = new();

            //    SharedStringTable sharedStringTable = sharedStringTablePart.SharedStringTable;
            //    foreach (string bandName in this.uniqueBandNames)
            //    {
            //        sharedStringTable.AppendChild(new SharedStringItem(new Text(bandName)));
            //    }

            //    sharedStringTable.Save();
            //}
        }
    }
}
