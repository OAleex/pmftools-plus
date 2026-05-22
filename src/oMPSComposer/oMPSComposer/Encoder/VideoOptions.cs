namespace oMPSComposer
{
    internal enum EncodeMode { OnePassVbr, TwoPassVbr }

    internal sealed class VideoOptions
    {
        public int AvgBitrate = 1000;
        public int MaxBitrate = 2000;

        public EncodeMode Mode = EncodeMode.TwoPassVbr;

        public int IdrDurationMs = 2000;
        public int MFrames = 1;
        public bool GradationImprove = true;
        public bool OverScan = false;
        public bool DeblockFilter = true;
        public bool SceneChangeDetect = true;
    }
}
