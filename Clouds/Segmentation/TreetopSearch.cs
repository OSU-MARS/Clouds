using Mars.Clouds.Cmdlets;
using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using OSGeo.OGR;
using System;
using System.Diagnostics;
using System.IO;

namespace Mars.Clouds.Segmentation
{
    internal abstract class TreetopSearch
    {
        public string? DiagnosticsPath { get; set; }
        public VirtualRaster<DigitalSurfaceModel> Dsm { get; private init; }
        public VirtualRaster<Raster<float>> Dtm { get; private init; }
        public float MinimumTreetopHeight { get; set; }

        public TreetopSearch()
        {
            this.DiagnosticsPath = null;
            this.Dsm = [];
            this.Dtm = [];
            this.MinimumTreetopHeight = Single.NaN;
        }

        public void AddTile(string dsmTilePath, string dtmTilePath)
        {
            DigitalSurfaceModel dsmTile = DigitalSurfaceModel.Read(dsmTilePath, loadData: true);
            Raster<float> dtmTile = Raster<float>.Read(dtmTilePath, readData: true);

            lock (this.Dsm)
            {
                this.Dsm.Add(dsmTile);
                this.Dtm.Add(dtmTile);
            }
        }

        public void BuildGrids()
        {
            if (SpatialReferenceExtensions.IsSameCrs(this.Dsm.Crs, this.Dtm.Crs) == false)
            {
                throw new NotSupportedException("The DSM and DTM are currently required to be in the same CRS. The DSM CRS is '" + this.Dsm.Crs.GetName() + "' while the DTM CRS is " + this.Dtm.Crs.GetName() + ".");
            }
            if (this.Dsm.IsSameExtentAndSpatialResolution(this.Dtm) == false)
            {
                throw new NotSupportedException("Since DTM resampling is not currently implemented, DSM and DTM rasters must be of the same size (width and height in cells) and have the same cell size (width and height in meters or feet). DSM cell size (" + this.Dsm.TileCellSizeX + ", " + this.Dsm.TileCellSizeY + "), DTM cell size (" + this.Dtm.TileCellSizeX + ", " + this.Dtm.TileCellSizeY + " " + this.Dtm.Crs.GetLinearUnitsPlural() + "). DSM tile size (" + this.Dsm.TileSizeInCellsX + ", " + this.Dsm.TileSizeInCellsY + "), DTM tile size (" + this.Dtm.TileSizeInCellsX + ", " + this.Dtm.TileSizeInCellsY + ") cells.");
            }

            // index tiles spatially
            this.Dsm.CreateTileGrid();
            this.Dtm.CreateTileGrid();
            if ((this.Dsm.TileTransform.OriginX != this.Dtm.TileTransform.OriginX) ||
                (this.Dsm.TileTransform.OriginY != this.Dtm.TileTransform.OriginY))
            {
                throw new NotSupportedException("Since DTM resampling is not currently implemented, DSM and DTM rasters must have the same origin. The DSM origin (" + this.Dsm.TileTransform.OriginX + ", " + this.Dsm.TileTransform.OriginY + ") is offset from the DTM origin (" + this.Dtm.TileTransform.OriginX + ", " + this.Dtm.TileTransform.OriginY + ").");
            }
            if (this.Dsm.IsSameExtentAndSpatialResolution(this.Dsm) == false)
            {
                throw new NotSupportedException("Since DTM resampling is not currently implemented, DSM and DTM rasters must have the same extent and cell size.");
            }
        }

        public static TreetopSearch Create(TreetopDetectionMethod detectionMethod, string? dsmBandName, string? dtmBandName)
        {
            return detectionMethod switch
            {
                TreetopDetectionMethod.ChmRadius or
                TreetopDetectionMethod.DsmRadius => new TreetopRadiusSearch(dsmBandName, dtmBandName) { SearchChm = detectionMethod == TreetopDetectionMethod.ChmRadius },
                TreetopDetectionMethod.DsmRing => new TreetopRingSearch(dsmBandName, dtmBandName),
                _ => throw new NotSupportedException("Unhandled treetop detection method " + detectionMethod + ".")
            };
        }

        public abstract int FindTreetops(int tileIndexX, int tileIndexY, string treetopFilePath);
    }

    internal abstract class TreetopSearch<TSearchState> : TreetopSearch where TSearchState : TreetopTileSearchState
    {
        private readonly string? dsmBandName;
        private readonly string? dtmBandName;

        protected TreetopSearch(string? dsmBandName, string? dtmBandName) 
        {
            this.dsmBandName = dsmBandName;
            this.dtmBandName = dtmBandName;
        }

        private static void AddToLayer(TreetopLayer treetopLayer, TreetopTileSearchState tileSearch, int treeID, double yIndexFractional, double xIndexFractional, float groundElevationAtBaseOfTree, float treetopElevation, double treeRadiusInCrsUnits)
        {
            (double centroidX, double centroidY) = tileSearch.Dsm.Transform.GetProjectedCoordinate(xIndexFractional, yIndexFractional);
            float treeHeightInCrsUnits = treetopElevation - groundElevationAtBaseOfTree;
            Debug.Assert((groundElevationAtBaseOfTree > -200.0F) && (groundElevationAtBaseOfTree < 30000.0F) && (treeHeightInCrsUnits < 400.0F));
            treetopLayer.Add(treeID, centroidX, centroidY, groundElevationAtBaseOfTree, treeHeightInCrsUnits, treeRadiusInCrsUnits);
        }

        protected abstract TSearchState CreateSearchState(string tileName, VirtualRasterNeighborhood8<float> dsmNeighborhood, VirtualRasterNeighborhood8<float> dtmNeighborhood);

        public override int FindTreetops(int tileIndexX, int tileIndexY, string treetopFilePath)
        {
            VirtualRasterNeighborhood8<float> dsmNeighborhood = this.Dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, this.dsmBandName);
            VirtualRasterNeighborhood8<float> dtmNeighborhood = this.Dtm.GetNeighborhood8<float>(tileIndexX, tileIndexY, this.dtmBandName);
            DigitalSurfaceModel? dsmTile = this.Dsm[tileIndexX, tileIndexY];
            Debug.Assert(dsmTile != null);

            using TSearchState tileSearch = this.CreateSearchState(Tile.GetName(dsmTile.FilePath), dsmNeighborhood, dtmNeighborhood);

            // set default minimum height if one was not specified
            // Assumption here is that xy and z units match, which is not necessarily enforced.
            if (Single.IsNaN(tileSearch.MinimumCandidateHeight))
            {
                tileSearch.MinimumCandidateHeight = 1.5F / tileSearch.CrsLinearUnits; // 1.5 m, 4.92 ft
            }

            // search for treetops in this tile
            using DataSource treetopFile = OgrExtensions.Open(treetopFilePath);
            using TreetopLayer treetopLayer = TreetopLayer.CreateOrOverwrite(treetopFile, tileSearch.Dsm.Crs);
            double dsmCellSize = tileSearch.Dsm.Transform.GetCellSize();
            for (int dsmIndex = 0, dsmYindex = 0; dsmYindex < tileSearch.Dsm.SizeY; ++dsmYindex) // y for north up rasters
            {
                for (int dsmXindex = 0; dsmXindex < tileSearch.Dsm.SizeX; ++dsmIndex, ++dsmXindex) // x for north up rasters
                {
                    float dsmZ = tileSearch.Dsm.Data[dsmIndex];
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
                    (bool addTreetop, int localMaximaRadiusInCells) = this.FindTreetops(dsmXindex, dsmYindex, dsmZ, dtmElevation, tileSearch);
                    if (addTreetop)
                    {
                        (double cellX, double cellY) = tileSearch.Dsm.Transform.GetCellCenter(dsmXindex, dsmYindex);
                        treetopLayer.Add(tileSearch.NextTreeID++, cellX, cellY, dtmElevation, heightInCrsUnits, localMaximaRadiusInCells * dsmCellSize);
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
                        SimilarElevationGroup<float> equalHeightPatch = tileSearch.TreetopEqualHeightPatches[patchIndex];
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
                SimilarElevationGroup<float> equalHeightPatch = tileSearch.TreetopEqualHeightPatches[patchIndex];
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

        protected abstract (bool addTreetop, int localMaximaRadiusInCells) FindTreetops(int dsmXindex, int dsmYindex, float dsmZ, float dtmElevation, TSearchState searchState);
    }
}
