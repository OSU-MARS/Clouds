using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml;
using System.Collections.Generic;
using System.IO;
using System;

namespace Mars.Clouds.DiskSpd
{
    internal class DiskSpdLongformResults : XlsxSerializable
    {
        public float[] AverageReadLatencyMilliseconds { get; private init; }
        public float[] AverageWriteLatencyMilliseconds { get; private init; }
        public int[] BlockSize { get; private init; }
        public int Count { get; private set; }
        public float[] DurationInS { get; private init; }
        public string[] Host { get; private init; }
        public int[] QueueDepth { get; private init; }
        public int[] RandomSize { get; private init; }
        public int[] Targets { get; private init; }
        public string[] TargetPath { get; private init; }
        public int[] ThreadID { get; private init; }
        public int[] ThreadsPerFile { get; private init; }
        public long[] ReadBytes { get; private init; }
        public float[] ReadLatencyStdev { get; private init; }
        public long[] WriteBytes { get; private init; }
        public float[] WriteLatencyStdev { get; private init; }

        public DiskSpdLongformResults(List<Results> resultsByFile)
        {
            this.Count = 0;

            int capacity = 0;
            for (int fileIndex = 0; fileIndex < resultsByFile.Count; ++fileIndex)
            {
                capacity += resultsByFile[fileIndex].TimeSpan.Threads.Count;
            }

            this.Host = new string[capacity];
            this.Targets = new int[capacity];
            this.TargetPath = new string[capacity];
            this.DurationInS = new float[capacity];
            this.RandomSize = new int[capacity];
            this.BlockSize = new int[capacity];
            this.QueueDepth = new int[capacity];
            this.ThreadsPerFile = new int[capacity];
            this.ThreadID = new int[capacity];

            this.ReadBytes = new long[capacity];
            this.AverageReadLatencyMilliseconds = new float[capacity];
            this.ReadLatencyStdev = new float[capacity];

            this.WriteBytes = new long[capacity];
            this.AverageWriteLatencyMilliseconds = new float[capacity];
            this.WriteLatencyStdev = new float[capacity];

            SortedDictionary<string, ProfileTarget> targetByPath = [];
            for (int fileIndex = 0; fileIndex < resultsByFile.Count; ++fileIndex)
            {
                Results results = resultsByFile[fileIndex];
                string host = results.System.ComputerName;

                targetByPath.Clear();
                for (int profileTimeSpanIndex = 0; profileTimeSpanIndex < results.Profile.TimeSpans.Count; ++profileTimeSpanIndex)
                {
                    ProfileTimeSpan timeSpan = results.Profile.TimeSpans[profileTimeSpanIndex];
                    for (int targetIndex = 0; targetIndex < timeSpan.Targets.Count; ++targetIndex)
                    {
                        ProfileTarget target = timeSpan.Targets[targetIndex];
                        targetByPath.Add(target.Path, target);
                    }
                }

                float duration = results.TimeSpan.TestTimeSeconds;
                if (duration <= 0.0F)
                {
                    throw new ArgumentOutOfRangeException(nameof(resultsByFile), "File " + fileIndex + " has a test duration of " + duration + " s.");
                }
                for (int threadIndex = 0; threadIndex < results.TimeSpan.Threads.Count; ++threadIndex)
                {
                    Thread thread = results.TimeSpan.Threads[threadIndex];
                    ThreadTarget threadTarget = thread.Target;
                    string targetPath = threadTarget.Path;
                    ProfileTarget profileTarget = targetByPath[targetPath];

                    this.Host[this.Count] = host;
                    this.DurationInS[this.Count] = duration;
                    this.Targets[this.Count] = targetByPath.Count;

                    this.ThreadID[this.Count] = thread.ID;
                    this.TargetPath[this.Count] = targetPath;
                    this.RandomSize[this.Count] = profileTarget.Random;
                    this.BlockSize[this.Count] = profileTarget.BlockSize;
                    this.QueueDepth[this.Count] = profileTarget.RequestCount;
                    this.ThreadsPerFile[this.Count] = profileTarget.ThreadsPerFile;

                    this.ReadBytes[this.Count] = threadTarget.ReadBytes;
                    this.AverageReadLatencyMilliseconds[this.Count] = threadTarget.AverageReadLatencyMilliseconds;
                    this.ReadLatencyStdev[this.Count] = threadTarget.ReadLatencyStdev;

                    this.WriteBytes[this.Count] = threadTarget.WriteBytes;
                    this.AverageWriteLatencyMilliseconds[this.Count] = threadTarget.AverageWriteLatencyMilliseconds;
                    this.WriteLatencyStdev[this.Count] = threadTarget.WriteLatencyStdev;

                    ++this.Count;
                }
            }
        }

        private Row CreateDataRow(int index)
        {
            Row row = new() { RowIndex = (UInt32)(index + 2) };
            row.Append(new Cell() { CellReference = "A" + row.RowIndex, CellValue = new(this.Host[index]), DataType = CellValues.String });
            row.Append(new Cell() { CellReference = "B" + row.RowIndex, CellValue = new(this.RandomSize[index] == -1 ? "sequential" : "random"), DataType = CellValues.String });
            row.Append(new Cell() { CellReference = "C" + row.RowIndex, CellValue = new(this.BlockSize[index] / 1024.0F), DataType = CellValues.Number });
            row.Append(new Cell() { CellReference = "D" + row.RowIndex, CellValue = new(this.QueueDepth[index]), DataType = CellValues.Number });
            row.Append(new Cell() { CellReference = "E" + row.RowIndex, CellValue = new(this.ThreadsPerFile[index]), DataType = CellValues.Number });
            row.Append(new Cell() { CellReference = "F" + row.RowIndex, CellValue = new(this.Targets[index]), DataType = CellValues.Number });
            row.Append(new Cell() { CellReference = "G" + row.RowIndex, CellValue = new(this.TargetPath[index]), DataType = CellValues.String });
            row.Append(new Cell() { CellReference = "H" + row.RowIndex, CellValue = new(this.ThreadID[index]), DataType = CellValues.Number });

            long readBytes = this.ReadBytes[index];
            long writeBytes = this.WriteBytes[index];
            float duration = this.DurationInS[index];
            float readInMBs = readBytes == -1 ? 0.0F : readBytes / (1024 * 1024 * duration);
            float writeInMBs = writeBytes == -1 ? 0.0F : writeBytes / (1024 * 1024 * duration);
            row.Append(new Cell() { CellReference = "I" + row.RowIndex, CellValue = new(duration), DataType = CellValues.Number });
            row.Append(new Cell() { CellReference = "J" + row.RowIndex, CellValue = new(readInMBs), DataType = CellValues.Number });
            row.Append(new Cell() { CellReference = "K" + row.RowIndex, CellValue = new(writeInMBs), DataType = CellValues.Number });

            if (readInMBs > 0.0F)
            {
                row.Append(new Cell() { CellReference = "L" + row.RowIndex, CellValue = new(1000.0F * this.AverageReadLatencyMilliseconds[index]), DataType = CellValues.Number });
                row.Append(new Cell() { CellReference = "M" + row.RowIndex, CellValue = new(1000.0F * this.ReadLatencyStdev[index]), DataType = CellValues.Number });
            }
            if (writeInMBs > 0.0F)
            {
                row.Append(new Cell() { CellReference = "N" + row.RowIndex, CellValue = new(1000.0F * this.AverageWriteLatencyMilliseconds[index]), DataType = CellValues.Number });
                row.Append(new Cell() { CellReference = "O" + row.RowIndex, CellValue = new(1000.0F * this.WriteLatencyStdev[index]), DataType = CellValues.Number });
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
            Sheet diskSpdResultsSheet = new()
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                Name = "DiskSpd",
                SheetId = 1
            };
            sheets.Append(diskSpdResultsSheet);

            // header row
            Row headerRow = XlsxSerializable.CreateHeaderRow("host", "pattern", "size, kB", "queueDepth", "threadsPerFile", "files", "file", "thread", "duration, s", "read, MB/s", "write, MB/s", "read latency, μs", "read latency σ, μs", "write latency, μs", "write latency σ, μs");
            bandStatisticsData.Append(headerRow);

            // data rows
            for (int index = 0; index < this.Count; ++index)
            {
                bandStatisticsData.Append(this.CreateDataRow(index));
            }

            workbookPart.Workbook.Save();
        }
    }
}
