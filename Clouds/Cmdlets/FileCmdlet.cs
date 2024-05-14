using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    public class FileCmdlet : Cmdlet
    {
        protected static List<string> GetExistingFilePaths(List<string>? fileSearchPaths, string defaultExtension)
        {
            ArgumentNullException.ThrowIfNull(fileSearchPaths, nameof(fileSearchPaths));
            if (fileSearchPaths.Count < 1)
            {
                throw new ArgumentNullException(nameof(fileSearchPaths), "No file search paths were specified.");
            }

            List<string> filePaths = [];
            for (int pathIndex = 0; pathIndex < fileSearchPaths.Count; ++pathIndex)
            {
                string fileSearchPath = fileSearchPaths[pathIndex];
                string? directoryPath = String.Empty;
                string? fileSearchPattern = String.Empty;
                bool pathSpecifiesSingleFile = false;
                if (fileSearchPath.Contains('*', StringComparison.Ordinal) || fileSearchPath.Contains('?', StringComparison.Ordinal))
                {
                    // presence of wildcards indicates a set of files in some directory
                    directoryPath = Path.GetDirectoryName(fileSearchPath);
                    fileSearchPattern = Path.GetFileName(fileSearchPath);
                    fileSearchPattern ??= "*" + defaultExtension;
                }
                else if (Directory.Exists(fileSearchPath))
                {
                    // if path indicates an existing directory, search it with the default extension
                    directoryPath = fileSearchPath;
                    fileSearchPattern = "*" + defaultExtension;
                }
                else
                {
                    if (File.Exists(fileSearchPath) == false)
                    {
                        throw new ArgumentOutOfRangeException(nameof(fileSearchPaths), "Can't fully load files. Path '" + fileSearchPath + "' is not an existing file, existing directory, or wildcarded search path.");
                    }
                    filePaths.Add(fileSearchPath);
                    pathSpecifiesSingleFile = true;
                }

                if (pathSpecifiesSingleFile == false)
                {
                    if (String.IsNullOrWhiteSpace(directoryPath))
                    {
                        throw new ArgumentOutOfRangeException(nameof(fileSearchPaths), "Wildcarded file search path '" + fileSearchPath + "' doesn't contain a directory path.");
                    }

                    string[] filePathsInDirectory = Directory.GetFiles(directoryPath, fileSearchPattern);
                    if (filePathsInDirectory.Length == 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(fileSearchPaths), "Can't load files. No files matched '" + Path.Combine(directoryPath, fileSearchPattern) + "'.");
                    }
                    filePaths.AddRange(filePathsInDirectory);
                }
            }

            if (filePaths.Count == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fileSearchPaths), "No files loaded as no files matched search paths '" + String.Join("', '", fileSearchPaths) + "'.");
            }
            return filePaths;
        }
    }
}
