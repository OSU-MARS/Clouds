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
        public RasterNeighborhood8<float> SurfaceNeighborhood { get; private init; }

        public float MinimumCandidateHeight { get; set; }
        public int NextTreeID { get; set; }

        public int EqualHeightPatchCommitInterval { get; set; }
        public SimilarElevationGroup<float>? MostRecentEqualHeightPatch { get; set; }
        public List<SimilarElevationGroup<float>> TreetopEqualHeightPatches { get; private init; }

        public TreetopSearchState(DigitalSurfaceModel dsmTile, RasterNeighborhood8<float> dsmNeighborhood)
        {
            this.Dsm = dsmTile;

            this.CrsLinearUnits = (float)this.Dsm.Crs.GetLinearUnits(); // 1.0 if CRS uses meters, 0.3048 if CRS is in feet
            this.CellHeight = MathF.Abs((float)this.Dsm.Transform.CellHeight); // ensure positive cell height values
            this.CellWidth = (float)this.Dsm.Transform.CellWidth;
            this.SurfaceNeighborhood = dsmNeighborhood;

            this.MinimumCandidateHeight = Single.NaN;
            this.NextTreeID = 1;

            this.EqualHeightPatchCommitInterval = (int)(50.0F / (this.CrsLinearUnits * this.CellHeight));
            this.MostRecentEqualHeightPatch = null;
            this.TreetopEqualHeightPatches = [];
        }

        public void AddMostRecentEqualHeightPatchAsTreetop()
        {
            if (this.MostRecentEqualHeightPatch == null)
            {
                throw new InvalidOperationException("Attempt to accept the most recent equal height patch as a treetop when no patch has yet been found.");
            }

            SimilarElevationGroup<float> treetop = this.MostRecentEqualHeightPatch;
            treetop.ID = this.NextTreeID++;
            this.TreetopEqualHeightPatches.Add(treetop);
        }

        public bool OnEqualHeightPatch(int dsmXindex, int dsmYindex, float candidateZ, int searchXindex, int searchYindex, int radiusInCells)
        {
            // clear most recent patch if this is a different patch
            if ((this.MostRecentEqualHeightPatch != null) && (this.MostRecentEqualHeightPatch.Height != candidateZ))
            {
                this.MostRecentEqualHeightPatch = null;
            }

            bool newEqualHeightPatchFound = false;
            if (this.MostRecentEqualHeightPatch == null)
            {
                // check for existing patch
                // Since patches are added sequentially, the patches adjacent to the current search point will be
                // towards the ned of the list.
                for (int patchIndex = this.TreetopEqualHeightPatches.Count - 1; patchIndex >= 0; --patchIndex)
                {
                    SimilarElevationGroup<float> candidatePatch = this.TreetopEqualHeightPatches[patchIndex];
                    if (candidatePatch.Height != candidateZ)
                    {
                        continue;
                    }
                    if (candidatePatch.Contains(searchXindex, searchYindex))
                    {
                        this.MostRecentEqualHeightPatch = candidatePatch;
                        break;
                    }
                    else if (candidatePatch.Contains(dsmXindex, dsmYindex))
                    {
                        this.TryGetCanopyHeight(searchXindex, searchYindex, out float dtmSearchZ);

                        this.MostRecentEqualHeightPatch = candidatePatch;
                        this.MostRecentEqualHeightPatch.Add(searchXindex, searchYindex, dtmSearchZ);
                        break;
                    }
                }
                if (this.MostRecentEqualHeightPatch == null)
                {
                    float dtmElevation = candidateZ = this.Dsm.CanopyHeight[dsmXindex, dsmYindex];
                    this.TryGetCanopyHeight(searchXindex, searchYindex, out float dtmSearchZ);
                    this.MostRecentEqualHeightPatch = new(candidateZ, dsmYindex, dsmXindex, dtmElevation, searchYindex, searchXindex, dtmSearchZ, radiusInCells);

                    newEqualHeightPatchFound = true;
                }
            }
            else
            {
                if (this.MostRecentEqualHeightPatch.Contains(dsmXindex, dsmYindex) == false)
                {
                    this.TryGetCanopyHeight(dsmXindex, dsmYindex, out float dtmSearchZ);
                    this.MostRecentEqualHeightPatch.Add(dsmXindex, dsmYindex, dtmSearchZ);
                }
            }

            return newEqualHeightPatchFound;
        }

        private bool TryGetCanopyHeight(int searchXindex, int searchYindex, out float chmZ)
        {
            if (this.Dsm.CanopyHeight.TryGetValue(searchXindex, searchYindex, out chmZ) == false)
            {
                chmZ = Single.NaN;
            }

            return true;
        }
    }
}
