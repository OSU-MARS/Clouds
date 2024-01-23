using Mars.Clouds.GdalExtensions;
using System;
using System.Collections.Generic;

namespace Mars.Clouds.Segmentation
{
    internal class TreetopTileSearchState
    {
        public float CrsLinearUnits { get; private init; }
        public RasterBand<float> Dsm { get; private init; }
        public float DsmCellHeight { get; private init; }
        public float DsmCellWidth { get; private init; }
        public VirtualRasterNeighborhood8<float> DsmNeighborhood { get; private init; }
        public RasterBand<float> Dtm { get; private init; }
        public VirtualRasterNeighborhood8<float> DtmNeighborhood { get; private init; }

        public float MinimumCandidateHeight { get; set; }
        public int NextTreeID { get; set; }

        public int EqualHeightPatchCommitInterval { get; set; }
        public SameHeightPatch<float>? MostRecentEqualHeightPatch { get; set; }
        public List<SameHeightPatch<float>> TreetopEqualHeightPatches { get; private init; }

        public TreetopTileSearchState(VirtualRasterNeighborhood8<float> dsmNeighborhood, VirtualRasterNeighborhood8<float> dtmNeighborhood)
        {
            this.Dsm = dsmNeighborhood.Center;

            this.CrsLinearUnits = (float)this.Dsm.Crs.GetLinearUnits(); // 1.0 if CRS uses meters, 0.3048 if CRS is in feet
            this.DsmCellHeight = MathF.Abs((float)this.Dsm.Transform.CellHeight); // ensure positive cell height values
            this.DsmCellWidth = (float)this.Dsm.Transform.CellWidth;
            this.DsmNeighborhood = dsmNeighborhood;
            this.Dtm = dtmNeighborhood.Center;
            this.DtmNeighborhood = dtmNeighborhood;

            this.MinimumCandidateHeight = Single.NaN;
            this.NextTreeID = 1;

            this.EqualHeightPatchCommitInterval = (int)(50.0F / (this.CrsLinearUnits * this.DsmCellHeight));
            this.MostRecentEqualHeightPatch = null;
            this.TreetopEqualHeightPatches = [];
        }

        public void AddMostRecentEqualHeightPatchAsTreetop()
        {
            if (this.MostRecentEqualHeightPatch == null)
            {
                throw new InvalidOperationException("Attempt to accept the most recent equal height patch as a treetop when no patch has yet been found.");
            }

            SameHeightPatch<float> treetop = this.MostRecentEqualHeightPatch;
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
                    SameHeightPatch<float> candidatePatch = this.TreetopEqualHeightPatches[patchIndex];
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
                        this.TryGetDtmValueNoDataNan(searchXindex, searchYindex, out float dtmSearchZ);

                        this.MostRecentEqualHeightPatch = candidatePatch;
                        this.MostRecentEqualHeightPatch.Add(searchXindex, searchYindex, dtmSearchZ);
                        break;
                    }
                }
                if (this.MostRecentEqualHeightPatch == null)
                {
                    float dtmElevation = this.Dtm[dsmXindex, dsmYindex];
                    if (this.Dtm.IsNoData(dtmElevation))
                    {
                        dtmElevation = Single.NaN;
                    }

                    this.TryGetDtmValueNoDataNan(searchXindex, searchYindex, out float dtmSearchZ);
                    this.MostRecentEqualHeightPatch = new(candidateZ, dsmYindex, dsmXindex, dtmElevation, searchYindex, searchXindex, dtmSearchZ, radiusInCells);

                    newEqualHeightPatchFound = true;
                }
            }
            else
            {
                if (this.MostRecentEqualHeightPatch.Contains(dsmXindex, dsmYindex) == false)
                {
                    this.TryGetDtmValueNoDataNan(searchXindex, searchYindex, out float dtmSearchZ);
                    this.MostRecentEqualHeightPatch.Add(searchXindex, searchYindex, dtmSearchZ);
                }
            }

            return newEqualHeightPatchFound;
        }

        private bool TryGetDtmValueNoDataNan(int searchXindex, int searchYindex, out float dtmSearchZ)
        {
            if (this.DtmNeighborhood.TryGetValue(searchXindex, searchYindex, out dtmSearchZ) == false)
            {
                dtmSearchZ = Single.NaN;
            }

            return true;
        }
    }
}
