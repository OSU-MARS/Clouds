using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Vrt;
using OSGeo.GDAL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Xml;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Get, "Vrt")]
    public class GetVrt : GdalCmdlet
    {
        [Parameter(Mandatory = true, HelpMessage = "List of directory paths, including wildcards, to search for virtual raster tiles. Each directory is assumed to contain a distinct set of tiles.")]
        [ValidateNotNullOrEmpty]
        public List<string> TilePaths { get; set; }

        [Parameter(HelpMessage = "List of bands to include in the virtual raster. All available bands are included if not specified.")]
        public List<string> Bands { get; set; }

        [Parameter(Mandatory = true, HelpMessage = "Path to output .vrt file.")]
        public string Vrt { get; set; }

        [Parameter(HelpMessage = "Options for subdirectories and files under the specified path. Default is a 16 kB buffer and to ignore inaccessible and directories as otherwise the UnauthorizedAccessException raised blocks enumeration of all other files.")]
        public EnumerationOptions EnumerationOptions { get; set; }

        public GetVrt()
        {
            this.Bands = [];
            this.EnumerationOptions = new()
            {
                BufferSize = 16 * 1024,
                IgnoreInaccessible = true
            };
            this.TilePaths = [];
            this.Vrt = String.Empty;
        }

        private (VirtualRaster<Raster> firstVrt, int firstVrtIndex) CheckBands(List<VirtualRaster<Raster>> vrts, List<List<string>> vrtBandsByVrtIndex)
        {
            if (this.Bands.Count > 0)
            {
                int bandsFound = 0;
                HashSet<string> uniqueBandsFound = [];
                for (int vrtIndex = 0; vrtIndex < vrts.Count; ++vrtIndex)
                {
                    List<string> vrtBands = vrtBandsByVrtIndex[vrtIndex];
                    bandsFound += vrtBands.Count;

                    for (int bandIndex = 0; bandIndex < vrtBands.Count; ++bandIndex)
                    {
                        uniqueBandsFound.Add(vrtBands[bandIndex]);
                    }
                }

                if (uniqueBandsFound.Count != this.Bands.Count)
                {
                    throw new ParameterOutOfRangeException(nameof(this.Bands), "-" + nameof(this.Bands) + " specifies " + this.Bands.Count + " but " + bandsFound + " matching bands were found with " + uniqueBandsFound.Count + " unique names.");
                }
            }

            for (int vrtIndex = 0; vrtIndex < vrts.Count; ++vrtIndex)
            {
                if (vrtBandsByVrtIndex[vrtIndex].Count > 0)
                {
                    return (vrts[vrtIndex], vrtIndex);
                }
            }

            throw new ParameterOutOfRangeException(nameof(this.Bands), "Either the virtual raster tiles specified by -" + nameof(this.TilePaths) + " contain no bands or -" + nameof(this.Bands) + " specifies none of the bands present in the tiles.");
        }

        protected override void ProcessRecord()
        {
            string? vrtDatasetDirectory = Path.GetDirectoryName(this.Vrt);
            if (vrtDatasetDirectory == null)
            {
                throw new ParameterOutOfRangeException(nameof(this.Vrt), "-" + nameof(this.Vrt) + " does not contain a directory path.");
            }

            (List<VirtualRaster<Raster>> vrts, List<List<string>> vrtBandsByVrtIndex) = this.LoadVrts();
            (VirtualRaster<Raster> firstVrt, int firstVrtIndex) = this.CheckBands(vrts, vrtBandsByVrtIndex);

            // create .vrt
            VrtDataset vrtDataset = firstVrt.CreateDataset(vrtDatasetDirectory, vrtBandsByVrtIndex[firstVrtIndex]);
            for (int vrtIndex = firstVrtIndex + 1; vrtIndex < vrts.Count; ++vrtIndex)
            {
                vrtDataset.AppendBands(vrtDatasetDirectory, vrts[vrtIndex], vrtBandsByVrtIndex[vrtIndex]);
            }

            // write .vrt
            using XmlWriter writer = XmlWriter.Create(this.Vrt);
            vrtDataset.WriteXml(writer);

            base.ProcessRecord();
        }

        private VirtualRaster<Raster> LoadVrt(string vrtPath)
        {
            bool vrtPathIsDirectory = Directory.Exists(vrtPath);
            string? vrtDirectoryPath = vrtPathIsDirectory ? vrtPath : Path.GetDirectoryName(vrtPath);
            if (vrtDirectoryPath == null)
            {
                throw new ArgumentOutOfRangeException(nameof(vrtPath), "Virtual raster path '" + vrtPath + "' does not contain a directory.");
            }
            if (Directory.Exists(vrtDirectoryPath) == false)
            {
                throw new ArgumentOutOfRangeException(nameof(vrtPath), "Directory indicated by virtual raster path '" + vrtPath + "' does not exist.");
            }

            string? vrtSearchPattern = vrtPathIsDirectory ? "*" + Constant.File.GeoTiffExtension : Path.GetFileName(vrtPath);
            IEnumerable<string> tilePaths = vrtSearchPattern != null ? Directory.EnumerateFiles(vrtDirectoryPath, vrtSearchPattern) : Directory.EnumerateFiles(vrtDirectoryPath);
            VirtualRaster<Raster> vrt = [];
            foreach (string tilePath in tilePaths)
            {
                using Dataset tileDataset = Gdal.Open(tilePath, Access.GA_ReadOnly);
                Raster tile = Raster.Read(tileDataset, readData: false);
                vrt.Add(tile);
            }

            if (vrt.TileCount < 1)
            {
                throw new ParameterOutOfRangeException(nameof(this.TilePaths), "-" + nameof(this.TilePaths) + " does not specify any virtual raster tiles.");
            }
            vrt.CreateTileGrid();
            return vrt;
        }

        private (List<VirtualRaster<Raster>> vrts, List<List<string>> vrtBandsByVrtIndex) LoadVrts()
        {
            List<List<string>> vrtBandsByVrtIndex = [];
            List<VirtualRaster<Raster>> vrts = [];

            VirtualRaster<Raster>? previousVrt = null;
            for (int pathIndex = 0; pathIndex < this.TilePaths.Count; ++pathIndex)
            {
                VirtualRaster<Raster> vrt = this.LoadVrt(this.TilePaths[pathIndex]);
                if (previousVrt != null)
                {
                    // for now, require that tile sets be exactly matched
                    if (SpatialReferenceExtensions.IsSameCrs(vrt.Crs, previousVrt.Crs) == false)
                    {
                        throw new NotSupportedException("Virtual raster '" + this.TilePaths[pathIndex - 1] + "' is in '" + previousVrt.Crs.GetName() + "' while '" + this.TilePaths[pathIndex] + "' is in '" + vrt.Crs.GetName() + "'.");
                    }
                    if (vrt.IsSameExtentAndSpatialResolution(previousVrt) == false)
                    {
                        throw new NotSupportedException("Virtual raster '" + this.TilePaths[pathIndex - 1] + "' and '" + this.TilePaths[pathIndex] + "' differ in spatial extent or resolution. Sizes are " + previousVrt.VirtualRasterSizeInTilesX + " by " + previousVrt.VirtualRasterSizeInTilesY + " and " + vrt.VirtualRasterSizeInTilesX + " by " + vrt.VirtualRasterSizeInTilesY + " tiles with tiles being " + previousVrt.TileCellSizeX + " by " + previousVrt.TileSizeInCellsY + " and " + vrt.TileSizeInCellsX + " by " + vrt.TileSizeInCellsY + " cells, respectively.");
                    }
                }

                if (this.Bands.Count > 0)
                {
                    List<string> bandsFromVrt = [];
                    for (int bandIndex = 0; bandIndex < this.Bands.Count; ++bandIndex)
                    {
                        string bandName = this.Bands[bandIndex];
                        if (vrt.BandNames.Contains(bandName, StringComparer.Ordinal))
                        {
                            bandsFromVrt.Add(bandName);
                        }
                    }
                    vrtBandsByVrtIndex.Add(bandsFromVrt);
                }
                else
                {
                    vrtBandsByVrtIndex.Add(vrt.BandNames);
                }
                vrts.Add(vrt);
            }

            return (vrts, vrtBandsByVrtIndex);
        }
    }
}
