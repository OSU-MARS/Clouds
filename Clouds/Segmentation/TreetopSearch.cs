using Mars.Clouds.Cmdlets;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using OSGeo.OGR;
using System;
using System.Diagnostics;

namespace Mars.Clouds.Segmentation
{
    internal abstract class TreetopSearch : TileReadWriteStreaming<DigitalSurfaceModel, TileStreamPosition>
    {
        private readonly string? surfaceBandName;

        public VirtualRaster<DigitalSurfaceModel> Dsm { get; private init; }

        protected TreetopSearch(VirtualRaster<DigitalSurfaceModel> dsm, string? surfaceBandName, GridNullable<DigitalSurfaceModel> dsmGrid, bool[,] unpopulatedTileMapForRead, bool[,] unpopulatedTileMapForWrite, bool outputPathIsDirectory)
            : base(dsmGrid, unpopulatedTileMapForRead, new(dsmGrid, unpopulatedTileMapForWrite), outputPathIsDirectory)
        {
            this.surfaceBandName = surfaceBandName;

            this.Dsm = dsm;
        }

        private static void AddToLayer(TreetopVector treetopLayer, TreetopSearchState tileSearch, int treeID, double yIndexFractional, double xIndexFractional, float groundElevationAtBaseOfTree, float treetopElevation, double treeRadiusInCrsUnits)
        {
            (double centroidX, double centroidY) = tileSearch.Dsm.Transform.GetProjectedCoordinate(xIndexFractional, yIndexFractional);
            float treeHeightInCrsUnits = treetopElevation - groundElevationAtBaseOfTree;
            Debug.Assert((groundElevationAtBaseOfTree > -200.0F) && (groundElevationAtBaseOfTree < 30000.0F) && (treeHeightInCrsUnits < 400.0F));
            treetopLayer.Add(treeID, centroidX, centroidY, groundElevationAtBaseOfTree, treeHeightInCrsUnits, treeRadiusInCrsUnits);
        }

        public int FindTreetops(int tileIndexX, int tileIndexY, string tileName, string treetopFilePath)
        {
            DigitalSurfaceModel? dsmTile = this.Dsm[tileIndexX, tileIndexY];
            if (dsmTile == null)
            {
                throw new ArgumentOutOfRangeException($"{nameof(tileIndexX)}, {nameof(tileIndexY)}", $"DSM tile at position {tileIndexX}, {tileIndexY} is null.");
            }

            RasterNeighborhood8<float> surfaceNeighborhood = this.Dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, this.surfaceBandName);
            RasterNeighborhood8<float> chmNeighborhood = surfaceNeighborhood;
            if (String.Equals(this.surfaceBandName, DigitalSurfaceModel.CanopyHeightBandName) == false)
            {
                chmNeighborhood = this.Dsm.GetNeighborhood8<float>(tileIndexX, tileIndexY, DigitalSurfaceModel.CanopyHeightBandName);
            }
            TreetopSearchState tileSearch = new(dsmTile, surfaceNeighborhood, chmNeighborhood);

            // set default minimum height if one was not specified
            // Assumption here is that xy and z units match, which is not necessarily enforced.
            if (Single.IsNaN(tileSearch.MinimumCandidateHeight))
            {
                tileSearch.MinimumCandidateHeight = 1.5F / tileSearch.CrsProjectedLinearUnitInM; // 1.5 m, 4.92 ft
            }

            // search for treetops in this tile
            RasterBand<float> dsm = tileSearch.Dsm.Surface;
            double dsmCellSize = dsm.Transform.GetCellSize();
            RasterBand<float> chm = tileSearch.Dsm.CanopyHeight;
            using DataSource treetopFile = OgrExtensions.CreateOrOpenForWrite(treetopFilePath);
            using TreetopVector treetopLayer = TreetopVector.CreateOrOverwrite(treetopFile, dsm.Crs, tileName.Length);
            for (int dsmIndex = 0, dsmYindex = 0; dsmYindex < dsm.SizeY; ++dsmYindex) // y for north up rasters
            {
                for (int dsmXindex = 0; dsmXindex < dsm.SizeX; ++dsmIndex, ++dsmXindex) // x for north up rasters
                {
                    float dsmZ = dsm.Data[dsmIndex];
                    if (dsm.IsNoData(dsmZ))
                    {
                        continue;
                    }

                    float chmHeight = chm[dsmIndex];
                    if (chm.IsNoData(chmHeight) || (chmHeight < tileSearch.MinimumCandidateHeight))
                    {
                        continue;
                    }

                    // add treetop if this cell is a unique local maxima
                    (bool addTreetop, int localMaximaRadiusInCells) = this.FindTreetops(dsmXindex, dsmYindex, dsmZ, chmHeight, tileSearch);
                    if (addTreetop)
                    {
                        (double cellX, double cellY) = dsm.Transform.GetCellCenter(dsmXindex, dsmYindex);
                        float dtmZ = dsmZ - chmHeight;
                        treetopLayer.Add(tileSearch.NextTreeID++, cellX, cellY, dtmZ, chmHeight, localMaximaRadiusInCells * dsmCellSize);
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
                        TreetopSearch.AddToLayer(treetopLayer, tileSearch, equalHeightPatch.ID, centroidYindex, centroidXindex, centroidElevation, equalHeightPatch.Height, radiusInCrsUnits);
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
                TreetopSearch.AddToLayer(treetopLayer, tileSearch, equalHeightPatch.ID, centroidYindex, centroidXindex, centroidElevation, equalHeightPatch.Height, radiusInCrsUnits);
            }

            return tileSearch.NextTreeID - 1;
        }

        protected abstract (bool addTreetop, int localMaximaRadiusInCells) FindTreetops(int dsmXindex, int dsmYindex, float dsmZ, float dtmElevation, TreetopSearchState searchState);
    }
}
