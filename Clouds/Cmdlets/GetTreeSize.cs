using ClosedXML.Excel;
using Mars.Clouds.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "TreeSize")]
    public class GetTreeSize : Cmdlet
    {
        private readonly List<DirectorySize> directorySizes;

        [Parameter(Mandatory = true, HelpMessage = "Path at which to begin enumeration of directory sizes.")]
        [ValidateNotNullOrEmpty]
        public string Path { get; set; }

        [Parameter(HelpMessage = ".xlsx spreadsheet to summarize directories to.")]
        public string Spreadsheet { get; set; }

        [Parameter(HelpMessage = "Options for subdirectories and files under the specified path. Default is a 512 kB buffer and to ignore inaccessible and directories as otherwise the UnauthorizedAccessException raised blocks enumeration of all other files.")]
        public EnumerationOptions EnumerationOptions { get; set; }

        public GetTreeSize()
        {
            this.directorySizes = [];

            this.EnumerationOptions = new()
            {
                BufferSize = 512 * 1024,
                IgnoreInaccessible = true
            };
            this.Path = String.Empty;
            this.Spreadsheet = String.Empty;
        }

        private void EnumerateDirectoryFilesAndSubdirectories(DirectoryInfo directoryInfo)
        {
            DirectorySize directorySize = new(directoryInfo.FullName);
            foreach (FileInfo fileInfo in directoryInfo.EnumerateFiles("*", this.EnumerationOptions))
            {
                switch (fileInfo.Extension.ToLowerInvariant()) // assume case insensitive use of file extensions
                {
                    case ".fjdslam":
                        directorySize.Fjdslam += fileInfo.Length;
                        break;
                    case ".geoslam":
                        directorySize.Geoslam += fileInfo.Length;
                        break;
                    case ".jpeg":
                    case ".jpg":
                        directorySize.Jpeg += fileInfo.Length;
                        break;
                    case Constant.File.LasExtension:
                        directorySize.Las += fileInfo.Length;
                        break;
                    case ".laz":
                        directorySize.Laz += fileInfo.Length;
                        break;
                    case ".ldr":
                    case ".ldr~1":
                        directorySize.Ldr += fileInfo.Length;
                        break;
                    case Constant.File.GeoTiffExtension:
                    case ".tiff":
                        directorySize.Tiff += fileInfo.Length;
                        break;
                    default:
                        // do nothing
                        break;
                }

                directorySize.Total += fileInfo.Length;
            }

            this.directorySizes.Add(directorySize);

            if (this.Stopping)
            {
                return;
            }
            if (this.directorySizes.Count % 100 == 0)
            {
                this.WriteProgress(new ProgressRecord(0, "directory enumeration", $"{this.directorySizes.Count} directories..."));
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

            if (String.IsNullOrEmpty(this.Spreadsheet))
            {
                this.WriteObject(this.directorySizes);

                stopwatch.Stop();
                this.WriteVerbose($"Enumerated {this.directorySizes.Count} directories in {stopwatch.Elapsed.ToElapsedString()} ({(this.directorySizes.Count / stopwatch.Elapsed.TotalSeconds):0.0} directories/s)");
            }
            else
            {
                this.WriteProgress(new ProgressRecord(0, "file enumeration", $"Writing {this.directorySizes.Count} files to spreadsheet..."));
                DirectorySize.Write(this.directorySizes, this.Spreadsheet, this.Path);

                stopwatch.Stop();
                this.WriteVerbose($"Enumerated {this.directorySizes.Count} files and wrote them to spreadsheet in {stopwatch.Elapsed.ToElapsedString()} ({(this.directorySizes.Count / stopwatch.Elapsed.TotalSeconds):0.0} files/s)");
            }
        }

        private class DirectorySize : XlsxSerializable
        {
            public long Fjdslam { get; set; } // raw point cloud data from FJ Dynamics scanners (S1, S2, P1, P2, ...)
            public long Geoslam { get; set; } // raw point cloud data from GeoSLAM Zeb or other GeoSLAM scanners
            public long Jpeg { get; set; } // flight or other imagery, .jpg and .jpeg
            public long Ldr { get; set; } // DJI raw point clouds, .ldr and .ldr~1
            public long Las { get; set; } // .las uncompressed point clouds
            public long Laz { get; set; } // .laz compressed point clouds
            public string Path { get; private init; } // path to directory
            public long Tiff { get; set; } // GeoTIFFs, .tif and .tiff
            public long Total { get; set; } // total size of all files in directory

            public DirectorySize(string path)
            {
                this.Fjdslam = 0;
                this.Geoslam = 0; 
                this.Jpeg = 0;
                this.Las = 0;
                this.Laz = 0;
                this.Ldr = 0;
                this.Path = path;
                this.Tiff = 0;
                this.Total = 0;
            }

            public static void Write(List<DirectorySize> directories, string spreadsheetPath, string sourcePath)
            {
                XLWorkbook workbook = new();
                IXLWorksheet directoriesWorksheet = workbook.Worksheets.Add("directories");

                // header row
                directoriesWorksheet.Cell(1, 1).Value = "path";
                directoriesWorksheet.Cell(1, 2).Value = "total, GB";
                directoriesWorksheet.Cell(1, 3).Value = ".las, GB";
                directoriesWorksheet.Cell(1, 4).Value = ".laz, GB";
                directoriesWorksheet.Cell(1, 5).Value = ".jpeg, GB";
                directoriesWorksheet.Cell(1, 6).Value = ".tiff, GB";
                directoriesWorksheet.Cell(1, 7).Value = ".fdjslam, GB";
                directoriesWorksheet.Cell(1, 8).Value = ".geoslam, GB";
                directoriesWorksheet.Cell(1, 9).Value = ".ldr, GB";
                directoriesWorksheet.Row(1).Style.Font.SetBold(true);
                directoriesWorksheet.SheetView.FreezeRows(1);

                // file metadata
                for (int fileIndex = 0; fileIndex < directories.Count; ++fileIndex)
                {
                    DirectorySize directory = directories[fileIndex];

                    int rowIndex = fileIndex + 2;
                    directoriesWorksheet.Cell(rowIndex, 1).Value = System.IO.Path.GetRelativePath(sourcePath, directory.Path);
                    directoriesWorksheet.Cell(rowIndex, 2).Value = 1E-9 * directory.Total;
                    directoriesWorksheet.Cell(rowIndex, 3).Value = 1E-9 * directory.Las;
                    directoriesWorksheet.Cell(rowIndex, 4).Value = 1E-9 * directory.Laz;
                    directoriesWorksheet.Cell(rowIndex, 5).Value = 1E-9 * directory.Jpeg;
                    directoriesWorksheet.Cell(rowIndex, 6).Value = 1E-9 * directory.Tiff;
                    directoriesWorksheet.Cell(rowIndex, 7).Value = 1E-9 * directory.Fjdslam;
                    directoriesWorksheet.Cell(rowIndex, 8).Value = 1E-9 * directory.Geoslam;
                    directoriesWorksheet.Cell(rowIndex, 9).Value = 1E-9 * directory.Ldr;
                }

                directoriesWorksheet.Column(1).Width = 50; // path
                directoriesWorksheet.Column(2).Width = 11; // total
                directoriesWorksheet.Column(3).Width = 9; // .las
                directoriesWorksheet.Column(4).Width = 9; // .laz
                directoriesWorksheet.Column(5).Width = 11; // .jpeg
                directoriesWorksheet.Column(6).Width = 10; // .tiff
                directoriesWorksheet.Column(7).Width = 13; // .fjdslam
                directoriesWorksheet.Column(8).Width = 13.2; // .geoslam
                directoriesWorksheet.Column(9).Width = 9; // .ldr
                directoriesWorksheet.Range(1, 1, directories.Count + 1, 9).SetAutoFilter(true);
                // https://github.com/ClosedXML/ClosedXML/issues/2634
                // directoriesWorksheet.Range(2, 2, directories.Count + 1, 9).AddConditionalFormat().WhenGreaterThan(0).NumberFormat.NumberFormatId = (int)XLPredefinedFormat.Number.Precision2;
                directoriesWorksheet.Range(2, 2, directories.Count + 1, 9).AddConditionalFormat().WhenGreaterThan(0).NumberFormat.Format = "0.00";
                workbook.SaveAs(spreadsheetPath);
            }
        }
    }
}
