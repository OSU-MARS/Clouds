using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using System;
using System.Diagnostics;

namespace Mars.Clouds.Segmentation
{
    internal class TreetopRingSearch : TreetopSearch
    {
        protected TreetopRingSearch(VirtualRaster<DigitalSurfaceModel> dsm, string? surfaceBandName, GridNullable<DigitalSurfaceModel> dsmGrid, bool[,] unpopulatedTileMapForRead, bool[,] unpopulatedTileMapForWrite, bool outputPathIsDirectory)
            : base(dsm, surfaceBandName, dsmGrid, unpopulatedTileMapForRead, unpopulatedTileMapForWrite, outputPathIsDirectory)
        {
        }

        public static TreetopRingSearch Create(VirtualRaster<DigitalSurfaceModel> dsm, string? surfaceBandName, bool outputPathIsDirectory)
        {
            if (dsm.TileGrid == null)
            {
                throw new ArgumentException("DSM's grid has not been created.", nameof(dsm));
            }

            surfaceBandName ??= DigitalSurfaceModel.SurfaceBandName;
            bool[,] unpopulatedTileMapForRead = dsm.TileGrid.GetUnpopulatedCellMap();
            bool[,] unpopulatedTileMapForWrite = ArrayExtensions.Copy(unpopulatedTileMapForRead);
            return new(dsm, surfaceBandName, dsm.TileGrid, unpopulatedTileMapForRead, unpopulatedTileMapForWrite, outputPathIsDirectory);
        }

        protected override (bool addTreetop, int localMaximaRadiusInCells) FindTreetops(int indexX, int indexY, float dsmZ, float chmHeight, TreetopSearchState searchState)
        {
            Debug.Assert(searchState.CellHeight == searchState.CellWidth, "Rectangular DSM cells are not currently supported.");

            float heightInM = searchState.CrsLinearUnits * chmHeight;
            float searchRadiusInCrsUnits = Single.Min(0.045F * heightInM + 0.5F, 4.0F) / searchState.CrsLinearUnits; // max 4 m radius

            bool dsmCellInSimilarElevationGroup = false;
            double dsmCellSizeInCrsUnits = searchState.CellHeight;
            bool newEqualHeightPatchFound = false;
            int maxRingIndex = Int32.Min((int)(searchRadiusInCrsUnits / dsmCellSizeInCrsUnits + 0.5F), Ring.Rings.Count);
            //int maxInnerRingIndex = Int32.Min(Int32.Max(maxRingIndex / 2, 0), 2); // tested inclusively so 1, 2, or 3 cell radius = ring index 0, 1, or 2
            int maxInnerRingIndex = Int32.Max((int)Single.Round(maxRingIndex / 2 + 0.5F), 1);
            int minRingIndexToLog = maxRingIndex < 2 ? 0 : 1; // local maxima within 1 or 2 cell radius

            bool higherCellWithinInnerRings = false;
            Span<float> maxRingHeight = stackalloc float[maxRingIndex];
            Span<float> minRingHeight = stackalloc float[maxRingIndex];
            int maxRingIndexEvaluated = -1;
            RasterNeighborhood8<float> surfaceNeighborhood = searchState.SurfaceNeighborhood;
            for (int ringIndex = 0; ringIndex < maxRingIndex; ++ringIndex)
            {
                Ring ring = Ring.Rings[ringIndex];
                
                float ringMinimumZ = Single.MaxValue;
                float ringMaximumZ = Single.MinValue;
                for (int cellIndex = 0; cellIndex < ring.Count; ++cellIndex)
                {
                    int searchXindex = indexX + ring.XIndices[cellIndex];
                    int searchYindex = indexY + ring.YIndices[cellIndex];
                    if (surfaceNeighborhood.TryGetValue(searchXindex, searchYindex, out float searchZ) == false)
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
                            newEqualHeightPatchFound |= searchState.OnSimilarElevation(indexX, indexY, dsmZ, searchXindex, searchYindex, maxRingIndex);
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

            float netProminenceNormalized = netProminence / chmHeight;
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
