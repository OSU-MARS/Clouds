﻿using Mars.Clouds.GdalExtensions;
using System;
using System.Diagnostics;
using System.IO;

namespace Mars.Clouds.Segmentation
{
    internal class TreetopRingSearch : TreetopSearch<TreetopRingSearchState>
    {
        protected override TreetopRingSearchState CreateSearchState(VirtualRasterNeighborhood8<float> dsmNeighborhood, VirtualRasterNeighborhood8<float> dtmNeighborhood)
        {
            return new TreetopRingSearchState(dsmNeighborhood, dtmNeighborhood, String.IsNullOrWhiteSpace(this.DiagnosticsPath) == false)
            {
                MinimumCandidateHeight = this.MinimumHeight
            };
        }

        protected override (bool addTreetop, int radiusInCells) FindTreetops(int dsmXindex, int dsmYindex, float dsmZ, float dtmElevation, TreetopRingSearchState searchState)
        {
            float heightInCrsUnits = dsmZ - dtmElevation;
            float heightInM = searchState.CrsLinearUnits * heightInCrsUnits;
            float searchRadiusInCrsUnits = Single.Min(0.045F * heightInM + 0.5F, 5.0F) / searchState.CrsLinearUnits;

            bool dsmCellInEqualHeightPatch = false;
            bool higherCellWithinInnerRings = false;
            bool newEqualHeightPatchFound = false;
            int maxRingIndex = Int32.Min((int)(searchRadiusInCrsUnits / searchState.DsmCellHeight + 0.5F), Ring.Rings.Count);
            int maxInnerRingIndex = Int32.Max((int)Single.Round(maxRingIndex / 2 + 0.5F), 1);
            Span<float> maxRingHeight = stackalloc float[maxRingIndex];
            Span<float> minRingHeight = stackalloc float[maxRingIndex];
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
                            // by a single lower cell, sometimes several cells (largest observed instance of an equal height doublet has
                            // had a 2.7 m spacing).
                            dsmCellInEqualHeightPatch = true;
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
            }

            if (higherCellWithinInnerRings)
            {
                return (false, -1);
            }

            float netProminence = 0.0F;
            float totalRange = 0.0F;
            int maxTallerRingRadius = -1;
            bool tallerRingRadiusFound = false;
            for (int ringIndex = 0; ringIndex < maxRingIndex; ++ringIndex)
            {
                float ringProminence = dsmZ - maxRingHeight[ringIndex];
                float ringRange = maxRingHeight[ringIndex] - minRingHeight[ringIndex];

                netProminence += ringProminence;
                totalRange += ringRange;
                if ((tallerRingRadiusFound == false) && (ringProminence > 0.0F))
                {
                    maxTallerRingRadius = ringIndex;
                }
                else
                {
                    tallerRingRadiusFound = true;
                }
            }
            if (netProminence >= Single.MaxValue)
            {
                return (false, -1); // at least one ring (usually ring radius = 1) has no data
            }
            Debug.Assert((Single.IsNaN(netProminence) == false) && (netProminence > -100000.0F) && (netProminence < 10000.0F));

            float netProminenceNormalized = netProminence / heightInCrsUnits;

            if (searchState.RingData != null)
            {
                Debug.Assert((searchState.NetProminenceBand != null) && (searchState.RadiusBand != null) && (searchState.RangeProminenceRatioBand != null) && (searchState.TotalProminenceBand != null) && (searchState.TotalRangeBand != null));

                searchState.NetProminenceBand[dsmXindex, dsmYindex] = netProminenceNormalized;
                searchState.RadiusBand[dsmXindex, dsmYindex] = maxRingIndex;

                float rangeProminenceRatioNormalized = totalRange / (maxRingIndex * heightInCrsUnits * netProminence);
                searchState.RangeProminenceRatioBand[dsmXindex, dsmYindex] = rangeProminenceRatioNormalized;

                float totalProminenceNormalized = netProminence / (maxRingIndex * heightInCrsUnits);
                searchState.TotalProminenceBand[dsmXindex, dsmYindex] = totalProminenceNormalized;

                float totalRangeNormalized = totalRange / (maxRingIndex * heightInCrsUnits);
                searchState.TotalRangeBand[dsmXindex, dsmYindex] = totalRangeNormalized;
            }

            if (netProminenceNormalized <= 0.02F)
            {
                return (false, -1);
            }

            if (dsmCellInEqualHeightPatch)
            {
                if (newEqualHeightPatchFound)
                {
                    searchState.AddMostRecentEqualHeightPatchAsTreetop();
                }

                return (false, -1);
            }

            return (true, maxTallerRingRadius);
        }

        protected override void WriteDiagnostics(string tileName, TreetopRingSearchState ringSearch)
        {
            Debug.Assert(this.DiagnosticsPath != null);
            if (ringSearch.RingData == null)
            {
                throw new ArgumentOutOfRangeException(nameof(ringSearch), "No diagnostic data is available from tile's ring search.");
            }

            string diagnosticsRasterPath = Path.Combine(this.DiagnosticsPath, tileName + Constant.File.GeoTiffExtension);
            ringSearch.RingData.Write(diagnosticsRasterPath);
        }
    }
}