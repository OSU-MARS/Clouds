using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using System;
using System.Diagnostics;

namespace Mars.Clouds.Segmentation
{
    internal class TreetopRadiusSearch : TreetopSearch
    {
        public bool SearchCanopyHeightModel { get; set; }

        protected TreetopRadiusSearch(VirtualRaster<DigitalSurfaceModel> dsm, string? surfaceBandName, GridNullable<DigitalSurfaceModel> dsmGrid, bool[,] unpopulatedTileMapForRead, bool[,] unpopulatedTileMapForWrite, bool outputPathIsDirectory)
            : base(dsm, surfaceBandName, dsmGrid, unpopulatedTileMapForRead, unpopulatedTileMapForWrite, outputPathIsDirectory)
        {
            this.SearchCanopyHeightModel = false;
        }

        public static TreetopRadiusSearch Create(VirtualRaster<DigitalSurfaceModel> dsm, string? surfaceBandName, bool searchChm, bool outputPathIsDirectory)
        {
            if (dsm.TileGrid == null)
            {
                throw new ArgumentException("DSM's grid has not been created.", nameof(dsm));
            }

            surfaceBandName ??= searchChm ? DigitalSurfaceModel.CanopyHeightBandName : DigitalSurfaceModel.SurfaceBandName;
            bool[,] unpopulatedTileMapForRead = dsm.TileGrid.GetUnpopulatedCellMap();
            bool[,] unpopulatedTileMapForWrite = ArrayExtensions.Copy(unpopulatedTileMapForRead);
            return new(dsm, surfaceBandName, dsm.TileGrid, unpopulatedTileMapForRead, unpopulatedTileMapForWrite, outputPathIsDirectory)
            {
                SearchCanopyHeightModel = searchChm
            };
        }

        protected override (bool addTreetop, int localMaximaRadiusInCells) FindTreetops(int indexX, int indexY, float dsmZ, float chmHeight, TreetopSearchState searchState)
        {
            float heightInM = searchState.CrsProjectedLinearUnitInM * chmHeight;
            // float searchRadiusInM = 8.59F / (1.0F + MathF.Exp((58.72F - heightInM) / 19.42F)); // logistic regression at 0.025 quantile against boostrap crown radii estimates
            // float searchRadiusInM = 6.0F / (1.0F + MathF.Exp((49.0F - heightInM) / 18.5F)); // manual retune based on segmentation
            float searchRadiusInM = Single.Min(0.055F * heightInM + 0.4F, 5.0F); // manual retune based on segmentation
            float searchRadiusInCrsUnits = searchRadiusInM / searchState.CrsProjectedLinearUnitInM;

            // check if point is local maxima
            float candidateZ = this.SearchCanopyHeightModel ? chmHeight : dsmZ;
            bool cellInSimilarElevationGroup = false;
            bool higherCellFound = false;
            bool newEqualHeightPatchFound = false;
            RasterNeighborhood8<float> surfaceNeighborhood = searchState.SurfaceNeighborhood;
            int ySearchRadiusInCells = Math.Max((int)(searchRadiusInCrsUnits / searchState.CellHeight + 0.5F), 1);
            int localMaximaRadiusInCells = ySearchRadiusInCells;
            for (int searchYoffset = 0; searchYoffset <= ySearchRadiusInCells; ++searchYoffset)
            {
                for (int searchYsign = (searchYoffset != 0 ? -1 : 1); searchYsign <= 1; searchYsign += 2)
                {
                    int searchIndexY = indexY + searchYsign * searchYoffset;
                    // constrain column bounds to circular search based on row offset
                    float searchYoffsetInCrsUnits = searchYoffset * searchState.CellHeight;
                    int maxXoffsetInCells = 0;
                    if (MathF.Abs(searchYoffsetInCrsUnits) < searchRadiusInCrsUnits) // avoid NaN from Sqrt()
                    {
                        float xSearchDistanceInCrsUnits = MathF.Sqrt(searchRadiusInCrsUnits * searchRadiusInCrsUnits - searchYoffsetInCrsUnits * searchYoffsetInCrsUnits);
                        maxXoffsetInCells = (int)(xSearchDistanceInCrsUnits / searchState.CellWidth + 0.5F);
                    }
                    if ((maxXoffsetInCells < 1) && (Math.Abs(searchYoffset) < 2))
                    {
                        // enforce minimum search of eight immediate neighbors as checking for local maxima becomes meaningless
                        // if the search radius collapses to zero.
                        maxXoffsetInCells = 1;
                    }

                    for (int searchXoffset = -maxXoffsetInCells; searchXoffset <= maxXoffsetInCells; ++searchXoffset)
                    {
                        int searchIndexX = indexX + searchXoffset;
                        if (surfaceNeighborhood.TryGetValue(searchIndexX, searchIndexY, out float searchZ) == false)
                        {
                            continue;
                        }

                        if (searchZ > candidateZ) // check of cell against itself when searchRowOffset = searchColumnOffset = 0 deemed not worth testing for but would need to be addressed if > is changed to >=
                        {
                            // some other cell within search radius is higher, so exclude this cell as a local maxima
                            // Abort local search, move to next cell.
                            higherCellFound = true;
                            break;
                        }
                        else if ((searchZ == candidateZ) && Raster.IsNeighbor8(searchYoffset, searchXoffset))
                        {
                            // if a neighboring cell (eight way adjacency) is of equal height, grow an equal height patch
                            // Growth is currently cell by cell, relying on incremental and sequential search of raster. Will need
                            // to be adjusted if cell skipping is implemented.
                            // While equal height patches are most commonly two cells there may be three or more cells in a patch and,
                            // depending on how the patch is discovered during search, OnEqualHeightPatch() may be called multiple times
                            // around the DSM cell where the patch is discovered. Since the patch is created only on the first call to
                            // OnEqualHeightPatch() |= is therefore used with newEqualHeightPatchFound to avoid potentially creating both
                            // a patch and a treetop (or possibly multiple treetops, though that's unlikely).
                            cellInSimilarElevationGroup = true;
                            newEqualHeightPatchFound |= searchState.OnSimilarElevation(indexX, indexY, candidateZ, searchIndexX, searchIndexY, ySearchRadiusInCells);
                        }
                    }

                    if (higherCellFound)
                    {
                        break;
                    }
                }

                if (higherCellFound)
                {
                    localMaximaRadiusInCells = searchYoffset;
                    break;
                }
            }

            if (higherCellFound)
            {
                return (false, localMaximaRadiusInCells);
            }
            else if (cellInSimilarElevationGroup)
            {
                if (newEqualHeightPatchFound)
                {
                    Debug.Assert(searchState.MostRecentSimilarElevationGroup != null);
                    searchState.TreetopEqualHeightPatches.Add(searchState.MostRecentSimilarElevationGroup);
                }

                return (false, localMaximaRadiusInCells);
            }
            else
            {
                return (true, localMaximaRadiusInCells);
            }
        }
    }
}
