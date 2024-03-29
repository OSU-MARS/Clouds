﻿using Mars.Clouds.Extensions;
using Mars.Clouds.GdalExtensions;
using OSGeo.OGR;
using System;
using System.IO;

namespace Mars.Clouds.Segmentation
{
    internal class TreetopRingSearchState : TreetopTileSearchState, IDisposable
    {
        private bool isDisposed;
        private readonly DataSource? ringLayer;

        public RingLayer? RingDiagnostics { get; private init; }

        public TreetopRingSearchState(VirtualRasterNeighborhood8<float> dsmNeighborhood, VirtualRasterNeighborhood8<float> dtmNeighborhood, string? ringFilePath)
            : base(dsmNeighborhood, dtmNeighborhood)
        {
            if (ringFilePath != null)
            {
                string tileName = Tile.GetName(ringFilePath);
                this.ringLayer = File.Exists(ringFilePath) ? Ogr.Open(ringFilePath, update: 1) : Ogr.GetDriverByName("GPKG").CreateDataSource(ringFilePath, null);
                this.RingDiagnostics = RingLayer.CreateOrOverwrite(this.ringLayer, dsmNeighborhood.Center.Crs, tileName);
            }
            else
            {
                this.ringLayer = null;
                this.RingDiagnostics = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                if (disposing)
                {
                    this.RingDiagnostics?.Dispose();
                    this.ringLayer?.Dispose();
                }

                this.isDisposed = true;
            }
        }
    }
}
