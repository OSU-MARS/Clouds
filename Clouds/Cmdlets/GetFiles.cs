using ClosedXML.Excel;
using DocumentFormat.OpenXml.Spreadsheet;
using Mars.Clouds.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "Files")]
    public class GetFiles: Cmdlet
    {
        private readonly List<FileInfo> fileInfo;
        private int lastFileCountReported;

        [Parameter(Mandatory = true, HelpMessage = "Path at which to begin enumeration of files.")]
        [ValidateNotNullOrEmpty]
        public string Path { get; set; }

        [Parameter(HelpMessage = ".xlsx spreadsheet to summarize files to.")]
        public string Spreadsheet { get; set; }

        [Parameter(HelpMessage = "Options for subdirectories and files under the specified path. Default is a 512 kB buffer and to ignore inaccessible and directories as otherwise the UnauthorizedAccessException raised blocks enumeration of all other files.")]
        public EnumerationOptions EnumerationOptions { get; set; }

        public GetFiles()
        {
            this.fileInfo = [];
            this.lastFileCountReported = 0;

            this.EnumerationOptions = new()
            {
                BufferSize = 512 * 1024,
                IgnoreInaccessible = true
            };
            this.Spreadsheet = String.Empty;
            this.Path = String.Empty;
        }

        private void EnumerateDirectoryFilesAndSubdirectories(DirectoryInfo directoryInfo)
        {
            this.fileInfo.AddRange(directoryInfo.EnumerateFiles("*", this.EnumerationOptions));

            if (this.Stopping)
            {
                return;
            }
            
            int filesAdded = this.fileInfo.Count - this.lastFileCountReported;
            if (filesAdded > 1000)
            {
                this.lastFileCountReported = 1000 * (this.fileInfo.Count / 1000);
                this.WriteProgress(new ProgressRecord(0, "file enumeration", this.lastFileCountReported + " files..."));
            }

            foreach (DirectoryInfo subdirectoryInfo in directoryInfo.EnumerateDirectories("*", this.EnumerationOptions))
            {
                this.EnumerateDirectoryFilesAndSubdirectories(subdirectoryInfo);
            }
        }

        protected override void ProcessRecord()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            this.EnumerateDirectoryFilesAndSubdirectories(new DirectoryInfo(this.Path));

            if (String.IsNullOrWhiteSpace(this.Spreadsheet))
            {
                this.WriteObject(this.fileInfo);
                
                stopwatch.Stop();
                this.WriteVerbose($"Enumerated {this.fileInfo.Count} files in {stopwatch.Elapsed.ToElapsedString()} ({(this.fileInfo.Count / stopwatch.Elapsed.TotalSeconds):0.0} files/s)");
            }
            else
            {
                this.WriteProgress(new ProgressRecord(0, "file enumeration", $"Writing {this.fileInfo.Count} files to spreadsheet..."));
                FilesSpreadsheet.Write(this.fileInfo, this.Spreadsheet, this.Path);

                stopwatch.Stop();
                this.WriteVerbose($"Enumerated {this.fileInfo.Count} files and wrote them to spreadsheet in {stopwatch.Elapsed.ToElapsedString()} ({(this.fileInfo.Count / stopwatch.Elapsed.TotalSeconds):0.0} files/s)");
            }
        }

        private class FilesSpreadsheet : XlsxSerializable
        {
            public static void Write(List<FileInfo> files, string spreadsheetPath, string sourcePath)
            {
                XLWorkbook workbook = new();
                IXLWorksheet filesWorksheet = workbook.Worksheets.Add("files");

                // header row
                filesWorksheet.Cell(1, 1).Value = "path";
                filesWorksheet.Cell(1, 2).Value = "size, bytes";
                filesWorksheet.Cell(1, 3).Value = "creationTime";
                filesWorksheet.Cell(1, 4).Value = "lastAccessTime";
                filesWorksheet.Cell(1, 5).Value = "lastWriteTime";
                filesWorksheet.Row(1).Style.Font.SetBold(true);
                filesWorksheet.SheetView.FreezeRows(1);

                // file metadata
                for (int fileIndex = 0; fileIndex < files.Count; ++fileIndex)
                {
                    FileInfo fileInfo = files[fileIndex];

                    int rowIndex = fileIndex + 2;
                    filesWorksheet.Cell(rowIndex, 1).Value = System.IO.Path.GetRelativePath(sourcePath, fileInfo.FullName);
                    filesWorksheet.Cell(rowIndex, 2).Value = fileInfo.Length;
                    filesWorksheet.Cell(rowIndex, 3).Value = fileInfo.CreationTime.ToOADate();
                    filesWorksheet.Cell(rowIndex, 4).Value = fileInfo.LastAccessTime.ToOADate();
                    filesWorksheet.Cell(rowIndex, 5).Value = fileInfo.LastWriteTime.ToOADate();
                }

                filesWorksheet.Column(1).Width = 75;
                filesWorksheet.Column(2).Width = 11;
                filesWorksheet.Columns(3, 5).Width = 19.9;
                filesWorksheet.Range(1, 1, files.Count + 1, 5).SetAutoFilter(true);
                filesWorksheet.Range(2, 3, files.Count + 1, 5).Style.DateFormat.Format = "yyyy\\-mm\\-dd\\Thh:mm:ss";
                workbook.SaveAs(spreadsheetPath);
            }
        }
    }
}
