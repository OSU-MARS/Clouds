using System;
using System.Collections.Generic;

namespace Mars.Clouds.Cmdlets
{
    public class TileEnumerator
    {
        private readonly float minimumSamplingFraction;
        private readonly int minimumTilesSampled;
        private readonly List<string>[] tilePathsByVrtIndex;

        public string Current { get; private set; }
        public bool SampleTile { get; private set; }
        public int TileIndexInVrt { get; private set; }
        public int VrtIndex { get; private set; }

        public TileEnumerator(List<string>[] tilePathsByVrtIndex, int minimumTilesSampled, float minimumSamplingFraction)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(minimumTilesSampled);
            ArgumentOutOfRangeException.ThrowIfNegative(minimumSamplingFraction);

            this.minimumSamplingFraction = minimumSamplingFraction;
            this.minimumTilesSampled = minimumTilesSampled;
            this.tilePathsByVrtIndex = tilePathsByVrtIndex;

            this.Current = String.Empty;
            this.TileIndexInVrt = -1;
            this.VrtIndex = -1;
        }

        public bool TryMoveTo(int tileIndex)
        {
            // could be made more efficient, but more complicated, by offsetting from previous tile index
            // Unclear if the complexity's worthwhile as often there's only one virtual raster and more than five rasters is rare,
            // so the number of avoidable loop iterations is low.
            int tileIndexOffsetForVrt = 0;
            for (int vrtIndex = 0; vrtIndex < tilePathsByVrtIndex.Length; ++vrtIndex)
            {
                this.TileIndexInVrt = tileIndex - tileIndexOffsetForVrt;
                List<string> tilePathsInVrt = this.tilePathsByVrtIndex[vrtIndex];

                if (this.TileIndexInVrt < tilePathsInVrt.Count) 
                {
                    this.Current = tilePathsInVrt[this.TileIndexInVrt];
                    this.VrtIndex = vrtIndex;

                    float samplingFraction = Single.Max((float)this.minimumTilesSampled / (float)tilePathsInVrt.Count, this.minimumSamplingFraction);
                    if (samplingFraction > 0.0F)
                    {
                        if (this.TileIndexInVrt == 0)
                        {
                            this.SampleTile = true;
                        }
                        else
                        {
                            // quantize sampling fraction to integer and sample each time the count increments
                            int previousTileSampleCount = (int)((this.TileIndexInVrt - 1) * samplingFraction);
                            int tileSampleCount = (int)(this.TileIndexInVrt * samplingFraction);
                            this.SampleTile = tileSampleCount != previousTileSampleCount;
                        }
                    }
                    else
                    {
                        this.SampleTile = false;
                    }
                    return true;
                }

                tileIndexOffsetForVrt += tilePathsInVrt.Count;
            }

            return false;
        }
    }
}