using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Threading;

namespace Mars.Clouds.Cmdlets
{
    public class FileCmdlet : PSCmdlet
    {
        protected CancellationTokenSource CancellationTokenSource { get; private init; }

        protected FileCmdlet()
        {
            this.CancellationTokenSource = new();
        }

        protected List<string> GetExistingFilePaths(List<string>? fileSearchPaths, string defaultExtension, SearchOption searchDepth = SearchOption.TopDirectoryOnly)
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
                        if (Path.IsPathRooted(fileSearchPath) == false)
                        {
                            string rootedFileSearchPath = Path.Combine(this.SessionState.Path.CurrentLocation.Path, fileSearchPath);
                            if (File.Exists(rootedFileSearchPath) == false)
                            {
                                throw new ArgumentOutOfRangeException(nameof(fileSearchPaths), "Can't fully load files. Path '" + fileSearchPath + "' is not an existing file, existing directory, or wildcarded search path which could not be found at PowerShell's current location of '" + rootedFileSearchPath + "'.");
                            }
                            fileSearchPath = rootedFileSearchPath;
                        }
                        else
                        {
                            throw new ArgumentOutOfRangeException(nameof(fileSearchPaths), "Can't fully load files. Path '" + fileSearchPath + "' is not an existing file, existing directory, or wildcarded search path. If the file exists this is usually an indication of a mismatch between the file path and PowerShell's current location.");
                        }
                    }

                    filePaths.Add(fileSearchPath);
                    pathSpecifiesSingleFile = true;
                }

                if (pathSpecifiesSingleFile == false)
                {
                    if (String.IsNullOrWhiteSpace(directoryPath))
                    {
                        directoryPath = this.SessionState.Path.CurrentLocation.Path;
                    }

                    string[] filePathsInDirectory = Directory.GetFiles(directoryPath, fileSearchPattern, searchDepth);
                    if (filePathsInDirectory.Length == 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(fileSearchPaths), "Can't load files. No files matched '" + Path.Combine(directoryPath, fileSearchPattern) + "'.");
                    }
                    filePaths.AddRange(filePathsInDirectory);
                }
            }

            if (filePaths.Count == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fileSearchPaths), "No files found as no files matched search paths '" + String.Join("', '", fileSearchPaths) + "'.");
            }
            return filePaths;
        }

        protected string GetRootedPath(string candidatePath)
        {
            if (Path.IsPathRooted(candidatePath))
            {
                return candidatePath;
            }

            return Path.Combine(this.SessionState.Path.CurrentFileSystemLocation.Path, candidatePath);
        }

        protected override void StopProcessing()
        {
            this.CancellationTokenSource.Cancel();
            base.StopProcessing();
        }
    }
}
