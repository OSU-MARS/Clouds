﻿using System.Collections.Generic;
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
        public string? Path { get; set; }

        public GetTreeSize()
        {
            this.directorySizes = new();
        }

        private void EnumerateDirectoryFilesAndSubdirectories(DirectoryInfo directoryInfo)
        {
            DirectorySize directorySize = new(directoryInfo.FullName);
            foreach (FileInfo fileInfo in directoryInfo.EnumerateFiles())
            {
                switch (fileInfo.Extension.ToLowerInvariant()) // assume case insensitive use of file extensions
                {
                    case ".geoslam":
                        directorySize.Geoslam += fileInfo.Length;
                        break;
                    case ".jpeg":
                    case ".jpg":
                        directorySize.Jpeg += fileInfo.Length;
                        break;
                    case ".las":
                        directorySize.Las += fileInfo.Length;
                        break;
                    case ".laz":
                        directorySize.Laz += fileInfo.Length;
                        break;
                    case ".ldr":
                    case ".ldr~1":
                        directorySize.Ldr += fileInfo.Length;
                        break;
                    case ".tif":
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

            if (this.directorySizes.Count % 100 == 0)
            {
                this.WriteProgress(new ProgressRecord(0, "directory enumeration", this.directorySizes.Count + " directories..."));
            }

            foreach (DirectoryInfo subdirectoryInfo in directoryInfo.EnumerateDirectories())
            {
                this.EnumerateDirectoryFilesAndSubdirectories(subdirectoryInfo);
            }
        }

        protected override void ProcessRecord()
        {
            Debug.Assert(this.Path != null);

            this.EnumerateDirectoryFilesAndSubdirectories(new DirectoryInfo(this.Path));

            this.WriteObject(this.directorySizes);
        }

        private class DirectorySize
        {
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
                this.Geoslam = 0; 
                this.Jpeg = 0;
                this.Las = 0;
                this.Laz = 0;
                this.Ldr = 0;
                this.Path = path;
                this.Tiff = 0;
                this.Total = 0;
            }
        }
    }
}