using Mars.Clouds.Cmdlets;
using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using OSGeo.OGR;
using System;
using System.Diagnostics;
using System.IO;

namespace Mars.Clouds.Segmentation
{
    internal abstract class TreetopSearch
    {
        public string? DiagnosticsPath { get; set; }
        public VirtualRaster<float> DsmTiles { get; private init; }
        public VirtualRaster<float> DtmTiles { get; private init; }
        public float MinimumHeight { get; set; }

        public TreetopSearch()
        {
            this.DiagnosticsPath = null;
            this.DsmTiles = new();
            this.DtmTiles = new();
            this.MinimumHeight = Single.NaN;
        }

        public void AddTile(string dsmTilePath, string dtmTilePath)
        {
            Raster<float> dsmTile = Raster<float>.Read(dsmTilePath);
            Raster<float> dtmTile = Raster<float>.Read(dtmTilePath);

            lock (this.DsmTiles)
            {
                this.DsmTiles.Add(dsmTile);
                this.DtmTiles.Add(dtmTile);
            }
        }

        public void BuildGrids()
        {
            if (SpatialReferenceExtensions.IsSameCrs(this.DsmTiles.Crs, this.DtmTiles.Crs) == false)
            {
                throw new NotSupportedException("The DSM and DTM are currently required to be in the same CRS. The DSM CRS is '" + this.DsmTiles.Crs.GetName() + "' while the DTM CRS is " + this.DtmTiles.Crs.GetName() + ".");
            }
            if (this.DsmTiles.IsSameSpatialResolutionAndExtent(this.DtmTiles) == false)
            {
                throw new NotSupportedException("Since DTM resampling is not currently implemented, DSM and DTM rasters must be of the same size (width and height in cells) and have the same cell size (width and height in meters or feet). DSM cell size (" + this.DsmTiles.TileCellSizeX + ", " + this.DsmTiles.TileCellSizeY + "), DTM cell size (" + this.DtmTiles.TileCellSizeX + ", " + this.DtmTiles.TileCellSizeY + " " + this.DtmTiles.Crs.GetLinearUnitsPlural() + "). DSM tile size (" + this.DsmTiles.TileSizeInCellsX + ", " + this.DsmTiles.TileSizeInCellsY + "), DTM tile size (" + this.DtmTiles.TileSizeInCellsX + ", " + this.DtmTiles.TileSizeInCellsY + ") cells.");
            }

            // index tiles spatially
            this.DsmTiles.BuildGrid();
            this.DtmTiles.BuildGrid();
            if ((this.DsmTiles.TileTransform.OriginX != this.DtmTiles.TileTransform.OriginX) ||
                (this.DsmTiles.TileTransform.OriginY != this.DtmTiles.TileTransform.OriginY))
            {
                throw new NotSupportedException("Since DTM resampling is not currently implemented, DSM and DTM rasters must have the same origin. The DSM origin (" + this.DsmTiles.TileTransform.OriginX + ", " + this.DsmTiles.TileTransform.OriginY + ") is offset from the DTM origin (" + this.DtmTiles.TileTransform.OriginX + ", " + this.DtmTiles.TileTransform.OriginY + ").");
            }
            if (this.DsmTiles.IsSameSpatialResolutionAndExtent(this.DsmTiles) == false)
            {
                throw new NotSupportedException("Since DTM resampling is not currently implemented, DSM and DTM rasters must have the same extent and cell size.");
            }
        }

        public static TreetopSearch Create(TreetopDetectionMethod detectionMethod)
        {
            return detectionMethod switch
            {
                TreetopDetectionMethod.ChmRadius or
                TreetopDetectionMethod.DsmRadius => new TreetopRadiusSearch() { SearchChm = detectionMethod == TreetopDetectionMethod.ChmRadius },
                TreetopDetectionMethod.DsmRing => new TreetopRingSearch(),
                _ => throw new NotSupportedException("Unhandled treetop detection method " + detectionMethod + ".")
            };
        }

        public abstract int FindTreetops(int tileIndex, string treetopFilePath);
    }

    internal abstract class TreetopSearch<TSearchState> : TreetopSearch where TSearchState : TreetopTileSearchState
    {
        private static void AddToLayer(TreetopLayer treetopLayer, TreetopTileSearchState tileSearch, int treeID, double yIndexFractional, double xIndexFractional, float groundElevationAtBaseOfTree, float treetopElevation, double treeRadiusInCrsUnits)
        {
            (double centroidX, double centroidY) = tileSearch.Dsm.Transform.GetProjectedCoordinate(xIndexFractional, yIndexFractional);
            float treeHeightInCrsUnits = treetopElevation - groundElevationAtBaseOfTree;
            Debug.Assert((groundElevationAtBaseOfTree > -200.0F) && (groundElevationAtBaseOfTree < 30000.0F) && (treeHeightInCrsUnits < 400.0F));
            treetopLayer.Add(treeID, centroidX, centroidY, groundElevationAtBaseOfTree, treeHeightInCrsUnits, treeRadiusInCrsUnits);
        }

        protected abstract TSearchState CreateSearchState(string tileName, VirtualRasterNeighborhood8<float> dsmNeighborhood, VirtualRasterNeighborhood8<float> dtmNeighborhood);

        public override int FindTreetops(int tileIndex, string treetopFilePath)
        {
            VirtualRasterNeighborhood8<float> dsmNeighborhood = this.DsmTiles.GetNeighborhood8(tileIndex, 0);
            VirtualRasterNeighborhood8<float> dtmNeighborhood = this.DtmTiles.GetNeighborhood8(tileIndex, 0);
            using TSearchState tileSearch = this.CreateSearchState(Tile.GetName(this.DsmTiles[tileIndex].FilePath), dsmNeighborhood, dtmNeighborhood);

            // set default minimum height if one was not specified
            // Assumption here is that xy and z units match, which is not necessarily enforced.
            if (Single.IsNaN(tileSearch.MinimumCandidateHeight))
            {
                tileSearch.MinimumCandidateHeight = 1.5F / tileSearch.CrsLinearUnits; // 1.5 m, 4.92 ft
            }

            // search for treetops in this tile
            using DataSource treetopFile = File.Exists(treetopFilePath) ? Ogr.Open(treetopFilePath, update: 1) : Ogr.GetDriverByName("GPKG").CreateDataSource(treetopFilePath, null);
            using TreetopLayer treetopLayer = TreetopLayer.CreateOrOverwrite(treetopFile, tileSearch.Dsm.Crs);
            double dsmCellSize = tileSearch.Dsm.Transform.GetCellSize();
            for (int dsmIndex = 0, dsmYindex = 0; dsmYindex < tileSearch.Dsm.YSize; ++dsmYindex) // y for north up rasters
            {
                for (int dsmXindex = 0; dsmXindex < tileSearch.Dsm.XSize; ++dsmIndex, ++dsmXindex) // x for north up rasters
                {
                    float dsmZ = tileSearch.Dsm.Data.Span[dsmIndex];
                    if (tileSearch.Dsm.IsNoData(dsmZ))
                    {
                        continue;
                    }

                    // read DTM interpolated to DSM resolution to get local height
                    // (double cellX, double cellY) = tileSearch.Dsm.Transform.GetCellCenter(dsmRowIndex, dsmColumnIndex);
                    // (int dtmRowIndex, int dtmColumnIndex) = dtm.Transform.GetCellIndex(cellX, cellY); 
                    float dtmElevation = tileSearch.Dtm[dsmXindex, dsmYindex]; // could use dsmIndex
                    if (tileSearch.Dtm.IsNoData(dtmElevation))
                    {
                        continue;
                    }
                    Debug.Assert(dtmElevation > -200.0F, "DTM elevation of " + dtmElevation + " is unexpectedly low.");

                    // get search radius and area for local maxima in DSM
                    // For now, use logistic quantile regression at p = 0.025 from prior Elliott State Research Forest segmentations.
                    float heightInCrsUnits = dsmZ - dtmElevation;
                    if (heightInCrsUnits < tileSearch.MinimumCandidateHeight)
                    {
                        continue;
                    }


                    // add treetop if this cell is a unique local maxima
                    (bool addTreetop, int radiusInCells) = this.FindTreetops(dsmXindex, dsmYindex, dsmZ, dtmElevation, tileSearch);
                    if (addTreetop)
                    {
                        (double cellX, double cellY) = tileSearch.Dsm.Transform.GetCellCenter(dsmXindex, dsmYindex);
                        treetopLayer.Add(tileSearch.NextTreeID++, cellX, cellY, dtmElevation, heightInCrsUnits, radiusInCells * dsmCellSize);
                    }
                }

                // perf: periodically commit patches which are many rows away and will no longer be added to
                // Could also do an immediate insert above and then upsert here once patches are fully discovered but doing so
                // appears low value.
                if ((dsmYindex > tileSearch.EqualHeightPatchCommitInterval) && (dsmYindex % tileSearch.EqualHeightPatchCommitInterval == 0))
                {
                    double maxPatchRowIndexToCommit = dsmYindex - tileSearch.EqualHeightPatchCommitInterval;
                    int maxPatchIndex = -1;
                    for (int patchIndex = 0; patchIndex < tileSearch.TreetopEqualHeightPatches.Count; ++patchIndex)
                    {
                        SameHeightPatch<float> equalHeightPatch = tileSearch.TreetopEqualHeightPatches[patchIndex];
                        (double centroidXindex, double centroidYindex, float centroidElevation, float radiusInCells) = equalHeightPatch.GetCentroid();
                        if (centroidYindex > maxPatchRowIndexToCommit)
                        {
                            break;
                        }

                        double radiusInCrsUnits = radiusInCells * dsmCellSize;
                        TreetopSearch<TSearchState>.AddToLayer(treetopLayer, tileSearch, equalHeightPatch.ID, centroidYindex, centroidXindex, centroidElevation, equalHeightPatch.Height, radiusInCrsUnits);
                        maxPatchIndex = patchIndex;
                    }

                    if (maxPatchIndex > -1)
                    {
                        tileSearch.TreetopEqualHeightPatches.RemoveRange(0, maxPatchIndex + 1);
                    }
                }
            }

            // create treetop points for equal height patches which haven't already been committed to layer
            for (int patchIndex = 0; patchIndex < tileSearch.TreetopEqualHeightPatches.Count; ++patchIndex)
            {
                SameHeightPatch<float> equalHeightPatch = tileSearch.TreetopEqualHeightPatches[patchIndex];
                (double centroidXindex, double centroidYindex, float centroidElevation, float radiusInCells) = equalHeightPatch.GetCentroid();

                double radiusInCrsUnits = radiusInCells * dsmCellSize;
                TreetopSearch<TSearchState>.AddToLayer(treetopLayer, tileSearch, equalHeightPatch.ID, centroidYindex, centroidXindex, centroidElevation, equalHeightPatch.Height, radiusInCrsUnits);
            }

            if (String.IsNullOrWhiteSpace(this.DiagnosticsPath) == false)
            {
                string? treetopFileNameWithoutExtension = Path.GetFileNameWithoutExtension(treetopFilePath);
                if (treetopFileNameWithoutExtension == null)
                {
                    throw new NotSupportedException("Treetop file path '" + treetopFilePath + " is not a path to a file.");
                }
            }

            return tileSearch.NextTreeID - 1;
        }

        protected abstract (bool addTreetop, int radiusInCells) FindTreetops(int dsmXindex, int dsmYindex, float dsmZ, float dtmElevation, TSearchState searchState);
    }
}
