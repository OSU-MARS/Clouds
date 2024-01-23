using Mars.Clouds.GdalExtensions;
using System.Diagnostics;
using System;

namespace Mars.Clouds.Segmentation
{
    internal class TreetopRadiusSearch : TreetopSearch<TreetopTileSearchState>
    {
        public bool SearchChm { get; set; }

        public TreetopRadiusSearch()
        {
            this.SearchChm = false;
        }

        protected override TreetopTileSearchState CreateSearchState(VirtualRasterNeighborhood8<float> dsmNeighborhood, VirtualRasterNeighborhood8<float> dtmNeighborhood)
        {
            return new(dsmNeighborhood, dtmNeighborhood)
            {
                MinimumCandidateHeight = this.MinimumHeight
            };
        }

        protected override (bool addTreetop, int radiusInCells) FindTreetops(int dsmXindex, int dsmYindex, float dsmZ, float dtmElevation, TreetopTileSearchState searchState)
        {
            float heightInCrsUnits = dsmZ - dtmElevation;
            float heightInM = searchState.CrsLinearUnits * heightInCrsUnits;
            // float searchRadiusInM = 8.59F / (1.0F + MathF.Exp((58.72F - heightInM) / 19.42F)); // logistic regression at 0.025 quantile against boostrap crown radii estimates
            // float searchRadiusInM = 6.0F / (1.0F + MathF.Exp((49.0F - heightInM) / 18.5F)); // manual retune based on segmentation
            float searchRadiusInM = Single.Min(0.055F * heightInM + 0.4F, 5.0F); // manual retune based on segmentation
            float searchRadiusInCrsUnits = searchRadiusInM / searchState.CrsLinearUnits;

            // check if point is local maxima
            float candidateZ = this.SearchChm ? heightInCrsUnits : dsmZ;
            bool dsmCellInEqualHeightPatch = false;
            bool higherCellFound = false;
            bool newEqualHeightPatchFound = false;
            int ySearchRadiusInCells = Math.Max((int)(searchRadiusInCrsUnits / searchState.DsmCellHeight + 0.5F), 1);
            for (int searchYoffset = -ySearchRadiusInCells; searchYoffset <= ySearchRadiusInCells; ++searchYoffset)
            {
                int searchYindex = dsmYindex + searchYoffset;
                // constrain column bounds to circular search based on row offset
                float searchYoffsetInCrsUnits = searchYoffset * searchState.DsmCellHeight;
                int maxXoffsetInCells = 0;
                if (MathF.Abs(searchYoffsetInCrsUnits) < searchRadiusInCrsUnits) // avoid NaN from Sqrt()
                {
                    float xSearchDistanceInCrsUnits = MathF.Sqrt(searchRadiusInCrsUnits * searchRadiusInCrsUnits - searchYoffsetInCrsUnits * searchYoffsetInCrsUnits);
                    maxXoffsetInCells = (int)(xSearchDistanceInCrsUnits / searchState.DsmCellWidth + 0.5F);
                }
                if ((maxXoffsetInCells < 1) && (Math.Abs(searchYoffset) < 2))
                {
                    // enforce minimum search of eight immediate neighbors as checking for local maxima becomes meaningless
                    // if the search radius collapses to zero.
                    maxXoffsetInCells = 1;
                }

                for (int searchXoffset = -maxXoffsetInCells; searchXoffset <= maxXoffsetInCells; ++searchXoffset)
                {
                    int searchXindex = dsmXindex + searchXoffset;
                    if (searchState.DsmNeighborhood.TryGetValue(searchXindex, searchYindex, out float dsmSearchZ) == false)
                    {
                        continue;
                    }

                    float searchZ = dsmSearchZ;
                    if (this.SearchChm)
                    {
                        if (searchState.DtmNeighborhood.TryGetValue(searchXindex, searchYindex, out float dtmZ) == false)
                        {
                            continue;
                        }
                        searchZ -= dtmZ;
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
                        dsmCellInEqualHeightPatch = true;
                        newEqualHeightPatchFound |= searchState.OnEqualHeightPatch(dsmXindex, dsmYindex, candidateZ, searchXindex, searchYindex, ySearchRadiusInCells);
                    }
                }

                if (higherCellFound)
                {
                    break;
                }
            }

            if (higherCellFound)
            {
                return (false, -1);
            }
            else if (dsmCellInEqualHeightPatch)
            {
                if (newEqualHeightPatchFound)
                {
                    Debug.Assert(searchState.MostRecentEqualHeightPatch != null);
                    searchState.TreetopEqualHeightPatches.Add(searchState.MostRecentEqualHeightPatch);
                }

                return (false, -1);
            }
            else
            {
                return (true, ySearchRadiusInCells);
            }
        }

        protected override void WriteDiagnostics(string tileName, TreetopTileSearchState tileState)
        {
            throw new NotSupportedException("Diagnostics path is set but diagnostics were not computed for radius search.");
        }
    }
}
