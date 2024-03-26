using Mars.Clouds.GdalExtensions;
using System;
using System.Diagnostics;
using System.IO;

namespace Mars.Clouds.Segmentation
{
    internal class TreetopRingSearch(string? dsmBandName, string? dtmBandName) : TreetopSearch<TreetopRingSearchState>(dsmBandName, dtmBandName)
    {
        protected override TreetopRingSearchState CreateSearchState(string tileName, VirtualRasterNeighborhood8<float> dsmNeighborhood, VirtualRasterNeighborhood8<float> dtmNeighborhood)
        {
            string? ringDiagnosticsFilePath = null;
            if (String.IsNullOrWhiteSpace(this.DiagnosticsPath) == false)
            {
                ringDiagnosticsFilePath = Path.Combine(this.DiagnosticsPath, tileName + Constant.File.GeoPackageExtension);
            }
            return new TreetopRingSearchState(dsmNeighborhood, dtmNeighborhood, ringDiagnosticsFilePath)
            {
                MinimumCandidateHeight = this.MinimumTreetopHeight
            };
        }

        protected override (bool addTreetop, int localMaximaRadiusInCells) FindTreetops(int dsmXindex, int dsmYindex, float dsmZ, float dtmElevation, TreetopRingSearchState searchState)
        {
            Debug.Assert(searchState.DsmCellHeight == searchState.DsmCellWidth, "Rectangular DSM cells are not currently supported.");

            double dsmCellSizeInCrsUnits = searchState.DsmCellHeight;
            float heightInCrsUnits = dsmZ - dtmElevation;
            float heightInM = searchState.CrsLinearUnits * heightInCrsUnits;
            float searchRadiusInCrsUnits = Single.Min(0.045F * heightInM + 0.5F, 4.0F) / searchState.CrsLinearUnits; // max 4 m radius

            bool dsmCellInSimilarElevationGroup = false;
            bool newEqualHeightPatchFound = false;
            int maxRingIndex = Int32.Min((int)(searchRadiusInCrsUnits / dsmCellSizeInCrsUnits + 0.5F), Ring.Rings.Count);
            //int maxInnerRingIndex = Int32.Min(Int32.Max(maxRingIndex / 2, 0), 2); // tested inclusively so 1, 2, or 3 cell radius = ring index 0, 1, or 2
            int maxInnerRingIndex = Int32.Max((int)Single.Round(maxRingIndex / 2 + 0.5F), 1);
            int minRingIndexToLog = maxRingIndex < 2 ? 0 : 1; // local maxima within 1 or 2 cell radius

            bool higherCellWithinInnerRings = false;
            Span<float> maxRingHeight = stackalloc float[maxRingIndex];
            Span<float> minRingHeight = stackalloc float[maxRingIndex];
            int maxRingIndexEvaluated = -1;
            for (int ringIndex = 0; ringIndex < maxRingIndex; ++ringIndex)
            {
                Ring ring = Ring.Rings[ringIndex];
                
                float ringMinimumZ = Single.MaxValue;
                float ringMaximumZ = Single.MinValue;
                for (int cellIndex = 0; cellIndex < ring.Count; ++cellIndex)
                {
                    int searchXindex = dsmXindex + ring.XIndices[cellIndex];
                    int searchYindex = dsmYindex + ring.YIndices[cellIndex];
                    if (searchState.DsmNeighborhood.TryGetValue(searchXindex, searchYindex, out float searchZ) == false)
                    {
                        continue;
                    }

                    if (ringIndex <= maxInnerRingIndex)
                    {
                        if (searchZ > dsmZ)
                        {
                            // some other cell within search radius is higher, so exclude this cell as a local maxima
                            // Abort local search, move to next cell.
                            higherCellWithinInnerRings = true;
                            break;
                        }
                        else if (searchZ == dsmZ)
                        {
                            // allow discontiguous equal height patches to avoid occasional spurious tops
                            // Most equal height patches are contiguous but larger trees sometimes have two equal height cells separated
                            // by a single lower cell, sometimes several cells. Largest observed instance of a valid equal height doublet
                            // has had a 2.7 m spacing.
                            // Problem: can produce a false or incorrectly placed treetop if not all cells in the patch are local maxima.
                            dsmCellInSimilarElevationGroup = true;
                            newEqualHeightPatchFound |= searchState.OnEqualHeightPatch(dsmXindex, dsmYindex, dsmZ, searchXindex, searchYindex, maxRingIndex);
                        }
                    }

                    if (searchZ < ringMinimumZ)
                    {
                        ringMinimumZ = searchZ;
                    }
                    if (searchZ > ringMaximumZ)
                    {
                        ringMaximumZ = searchZ;
                    }

                }

                if (higherCellWithinInnerRings)
                {
                    break;
                }

                maxRingHeight[ringIndex] = ringMaximumZ;
                minRingHeight[ringIndex] = ringMinimumZ;
                maxRingIndexEvaluated = ringIndex;
            }

            //if (higherCellWithinInnerRings)
            //{
            //    return (false, -1); // reduces computational costs by bypassing ring calculations
            //}

            float netProminence = 0.0F;
            float totalRange = 0.0F;
            int localMaximaRadiusInCells = 0;
            bool tallerRingRadiusFound = false;
            for (int ringIndex = 0; ringIndex <= maxRingIndexEvaluated; ++ringIndex)
            {
                float ringProminence = dsmZ - maxRingHeight[ringIndex];
                float ringRange = maxRingHeight[ringIndex] - minRingHeight[ringIndex];

                netProminence += ringProminence;
                totalRange += ringRange;
                if ((tallerRingRadiusFound == false) && (ringProminence > 0.0F))
                {
                    localMaximaRadiusInCells = ringIndex + 1;
                }
                else
                {
                    tallerRingRadiusFound = true;
                }
            }
            if (netProminence >= Single.MaxValue)
            {
                return (false, -1); // at least one evaluated ring has no data (most likely ring radius = 1 as it contains the fewest cells)
            }
            Debug.Assert((Single.IsNaN(netProminence) == false) && (netProminence > -100000.0F) && (netProminence < 10000.0F));

            float netProminenceNormalized = netProminence / heightInCrsUnits;
            if ((searchState.RingDiagnostics != null) && (maxRingIndexEvaluated >= minRingIndexToLog))
            {
                (double x, double y) = searchState.Dsm.Transform.GetCellCenter(dsmXindex, dsmYindex);
                double localMaximaRadiusInCrsUnits = dsmCellSizeInCrsUnits * localMaximaRadiusInCells;
                searchState.RingDiagnostics.Add(searchState.NextTreeID, x, y, dtmElevation, dsmZ, dsmCellInSimilarElevationGroup, localMaximaRadiusInCrsUnits, netProminenceNormalized, maxRingIndexEvaluated, maxRingHeight, minRingHeight);
            }

            if (higherCellWithinInnerRings || (netProminenceNormalized <= 0.02F))
            {
                return (false, -1);
            }

            if (dsmCellInSimilarElevationGroup)
            {
                if (newEqualHeightPatchFound)
                {
                    searchState.AddMostRecentEqualHeightPatchAsTreetop();
                }

                return (false, -1);
            }

            return (true, localMaximaRadiusInCells);
        }
    }
}
