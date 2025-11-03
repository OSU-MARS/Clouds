using Mars.Clouds.Las;
using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace Mars.Clouds.Cmdlets
{
    [Cmdlet(VerbsCommon.Set, "LasHeader")]
    public class SetLasHeader : LasCmdlet
    {
        [Parameter(HelpMessage = "Value to set x offset in point clouds' header to.")]
        public double XOffset { get; set; }

        [Parameter(HelpMessage = "Value to set y offset in point clouds' header to.")]
        public double YOffset { get; set; }

        [Parameter(HelpMessage = "Value to set z offset in point clouds' header to.")]
        public double ZOffset { get; set; }

        public SetLasHeader()
        {
            this.XOffset = Double.NaN;
            this.YOffset = Double.NaN;
            this.ZOffset = Double.NaN;
        }

        protected override void ProcessRecord()
        {
            bool hasChange = (Double.IsNaN(this.XOffset) == false) || (Double.IsNaN(this.YOffset) == false) || (Double.IsNaN(this.ZOffset) == false);
            if (hasChange == false)
            {
                throw new ParameterBindingException($"At least one of -{nameof(this.XOffset)}, -{nameof(this.YOffset)}, or -{nameof(this.ZOffset)} must be specified to modify the point cloud(s) specified by -{nameof(this.Las)}.");
            }

            List<string> lasFilePaths = this.GetExistingFilePaths(this.Las, Constant.File.LasExtension);
            for (int fileIndex = 0; fileIndex < lasFilePaths.Count; ++fileIndex)
            {
                // can multithread if needed but doesn't seem worth the overhead up to a few hundred .las files
                string lasFilePath = lasFilePaths[fileIndex];
                using LasReader lasReader = LasReader.CreateForHeaderAndVlrReadAndWrite(lasFilePath, this.DiscardOverrunningVlrs);
                LasTile lasFile = new(lasFilePath, lasReader, fallbackCreationDate: null);

                bool lasModified = false;
                if ((Double.IsNaN(this.XOffset) == false) && (lasFile.Header.XOffset != this.XOffset))
                {
                    double xTranslation = this.XOffset - lasFile.Header.XOffset;
                    lasFile.Header.XOffset = this.XOffset;
                    lasFile.Header.MinX += xTranslation;
                    lasFile.Header.MaxX += xTranslation;
                    lasModified = true;
                }
                if ((Double.IsNaN(this.YOffset) == false) && (lasFile.Header.YOffset != this.YOffset))
                {
                    double yTranslation = this.YOffset - lasFile.Header.YOffset;
                    lasFile.Header.YOffset = this.YOffset;
                    lasFile.Header.MinY += yTranslation;
                    lasFile.Header.MaxY += yTranslation;
                    lasModified = true;
                }
                if ((Double.IsNaN(this.ZOffset) == false) && (lasFile.Header.ZOffset != this.ZOffset))
                {
                    double zTranslation = this.ZOffset - lasFile.Header.ZOffset;
                    lasFile.Header.ZOffset = this.ZOffset;
                    lasFile.Header.MinZ += zTranslation;
                    lasFile.Header.MaxZ += zTranslation;
                    lasModified = true;
                }

                if (lasModified)
                {
                    using LasWriter writer = lasReader.AsWriter();
                    writer.WriteHeader(lasFile);
                }

                if (this.Stopping)
                {
                    return;
                }
            }

            base.ProcessRecord();
        }
    }
}
