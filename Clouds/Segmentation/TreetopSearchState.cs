using Mars.Clouds.GdalExtensions;
using Mars.Clouds.Las;
using System;
using System.Collections.Generic;

namespace Mars.Clouds.Segmentation
{
    internal class TreetopSearchState
    {
        public float CrsLinearUnits { get; private init; }
        public DigitalSurfaceModel Dsm { get; private init; }
        public float CellHeight { get; private init; }
        public float CellWidth { get; private init; }
        public RasterNeighborhood8<float> ChmNeighborhood { get; private init; }
        public RasterNeighborhood8<float> SurfaceNeighborhood { get; private init; }

        public float MinimumCandidateHeight { get; set; }
        public int NextTreeID { get; set; }

        public int EqualHeightPatchCommitInterval { get; set; }
        public SimilarElevationGroup<float>? MostRecentSimilarElevationGroup { get; set; }
        public List<SimilarElevationGroup<float>> TreetopEqualHeightPatches { get; private init; }

        public TreetopSearchState(DigitalSurfaceModel dsmTile, RasterNeighborhood8<float> surfaceNeighborhood, RasterNeighborhood8<float> chmNeighborhood)
        {
            this.Dsm = dsmTile;

            this.CrsLinearUnits = (float)this.Dsm.Crs.GetLinearUnits(); // 1.0 if CRS uses meters, 0.3048 if CRS is in feet
            this.CellHeight = MathF.Abs((float)this.Dsm.Transform.CellHeight); // ensure positive cell height values
            this.CellWidth = (float)this.Dsm.Transform.CellWidth;
            this.ChmNeighborhood = chmNeighborhood;
            this.SurfaceNeighborhood = surfaceNeighborhood;

            this.MinimumCandidateHeight = Single.NaN;
            this.NextTreeID = 1;

            this.EqualHeightPatchCommitInterval = (int)(50.0F / (this.CrsLinearUnits * this.CellHeight));
            this.MostRecentSimilarElevationGroup = null;
            this.TreetopEqualHeightPatches = [];
        }

        public void AddMostRecentEqualHeightPatchAsTreetop()
        {
            if (this.MostRecentSimilarElevationGroup == null)
            {
                throw new InvalidOperationException("Attempt to accept the most recent equal height patch as a treetop when no patch has yet been found.");
            }

            SimilarElevationGroup<float> treetop = this.MostRecentSimilarElevationGroup;
            treetop.ID = this.NextTreeID++;
            this.TreetopEqualHeightPatches.Add(treetop);
        }

        public bool OnSimilarElevation(int dsmXindex, int dsmYindex, float surfaceZ, int searchXindex, int searchYindex, int radiusInCells)
        {
            // clear most recent patch if this is a different patch
            if ((this.MostRecentSimilarElevationGroup != null) && (this.MostRecentSimilarElevationGroup.Height != surfaceZ))
            {
                this.MostRecentSimilarElevationGroup = null;
            }

            bool newEqualHeightPatchFound = false;
            if (this.MostRecentSimilarElevationGroup == null)
            {
                // check for existing patch
                // Since patches are added sequentially, the patches adjacent to the current search point will be
                // towards the ned of the list.
                for (int patchIndex = this.TreetopEqualHeightPatches.Count - 1; patchIndex >= 0; --patchIndex)
                {
                    SimilarElevationGroup<float> candidatePatch = this.TreetopEqualHeightPatches[patchIndex];
                    if (candidatePatch.Height != surfaceZ)
                    {
                        continue;
                    }
                    if (candidatePatch.Contains(searchXindex, searchYindex))
                    {
                        this.MostRecentSimilarElevationGroup = candidatePatch;
                        break;
                    }
                    else if (candidatePatch.Contains(dsmXindex, dsmYindex))
                    {
                        if (this.SurfaceNeighborhood.TryGetValue(searchXindex, searchYindex, out float surfaceSearchZ) && this.ChmNeighborhood.TryGetValue(searchXindex, searchYindex, out float chmSearchZ))
                        {
                            float dtmSearchZ = surfaceSearchZ - chmSearchZ;
                            this.MostRecentSimilarElevationGroup = candidatePatch;
                            this.MostRecentSimilarElevationGroup.Add(searchXindex, searchYindex, dtmSearchZ);
                        }
                        break;
                    }
                }
                if (this.MostRecentSimilarElevationGroup == null)
                {
                    if (this.SurfaceNeighborhood.TryGetValue(searchXindex, searchYindex, out float surfaceSearchZ) && this.ChmNeighborhood.TryGetValue(searchXindex, searchYindex, out float chmSearchZ))
                    {
                        float dtmElevation = surfaceZ - this.Dsm.CanopyHeight[dsmXindex, dsmYindex];
                        float dtmSearchZ = surfaceSearchZ - chmSearchZ;
                        this.MostRecentSimilarElevationGroup = new(surfaceZ, dsmYindex, dsmXindex, dtmElevation, searchYindex, searchXindex, dtmSearchZ, radiusInCells);

                        newEqualHeightPatchFound = true;
                    }
                }
            }
            else
            {
                if (this.MostRecentSimilarElevationGroup.Contains(dsmXindex, dsmYindex) == false)
                {
                    float dtmElevation = surfaceZ - this.Dsm.CanopyHeight[dsmXindex, dsmYindex];
                    this.MostRecentSimilarElevationGroup.Add(dsmXindex, dsmYindex, dtmElevation);
                }
            }

            return newEqualHeightPatchFound;
        }
    }
}
