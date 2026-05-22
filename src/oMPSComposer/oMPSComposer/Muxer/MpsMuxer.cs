using System;
using System.Collections.Generic;
using System.IO;

namespace oMPSComposer
{
    internal static class MpsMuxer
    {
        private const int PackSize = 2048;
        private const uint MuxRate = 25_000;
        private const ulong Clock27M = 27_000_000;
        private const ulong PackDurationDen = MuxRate * 50UL;
        private const ulong PackDurationNum = (ulong)PackSize * Clock27M;
        private const ulong PackDurationSeedRem = 1_000_000;
        private const ulong PackDurationStartupSeedRem = 250_000;

        private const ulong PtsVideoStart = 90_000;
        private const ulong PtsAudioStart = 85_069;
        private const int PtsStampInterval = 16;
        private const int PtsStampIntervalFinal = 18;
        private const ulong FrameDuration90K = 3003;
        private const int DeepContinuationStartFrame = 3_770;
        private const int LateTimestampGateStartFrame = 5_000;
        private const int TailTimestampGateMaxFramesRemaining = 100;
        private const int DeepContinuationMinBytesToTarget = 256;

        private const ulong ScrLeadVideoSeg = 89_850 * 300;
        private const ulong ScrLeadVideoMid = 90_000 * 300;
        private const ulong ScrLeadVideoCross = 86_958 * 300;
        private const ulong ScrLeadVideoStartup = 10_782_564;
        private const ulong ScrLeadVideoInitialStartupTail = 22_470_000;
        private const ulong ScrLeadVideoStartupTail = 77_920 * 300;
        private const ulong ScrLeadAudioBoot0 = 74_775 * 300;
        private const ulong ScrLeadAudioBoot1 = 24_468_692;
        private const ulong ScrLeadAudioNorm = 16_719 * 300;
        private const ulong ScrLeadAudioShort = 12_537 * 300;
        private const ulong AudioPlainRaceTolerance27M = 8_192;
        private const ulong VideoBfWaitTolerance27M = AudioPlainRaceTolerance27M;
        private const ulong DeepContinuationTolerance27M = AudioPlainRaceTolerance27M * 3 + 2_048;

        private const ulong ScrInitial27M = 3_044_236;
        private const ulong ScrInitialStartup27M = 15_342_067;

        private const int VideoPayload = PackSize - 14 - 9;
        private const int AudioPayloadFirst = PackSize - 14 - 6 - 3 - 8 - 4;
        private const int AudioPayloadNorm = PackSize - 14 - 6 - 3 - 5 - 4;

        private const byte SidVideo = 0xE0;
        private const byte SidAudio = 0xBD;
        private const byte SidSystem = 0xBB;
        private const byte SidPriv2 = 0xBF;
        private const byte SidPadding = 0xBE;
        public const int MaxAudioTracks = 9;

        private const int NoDeepContinuationGate = -1;

        private static readonly int[] DeepContinuationMaxBytesToTargetByFrameOffset =
        {
            NoDeepContinuationGate,
            1776, 4027, 13420, 20594, 30036, 36910, 41698, 48372,
            53035, 57432, 62351, 67627, 69754, 71660, 76598, 76547,
            76773, 75166, 69190, 63319, 56996, 50960, 44752, 34977,
            28118, 23452, 16838, 8010, 1799, 1107, 1338, NoDeepContinuationGate, 1331,
            NoDeepContinuationGate, 878, 61, 1375, 1657, 1706, 612, NoDeepContinuationGate, 322, 1400,
            1832, 1718, 385, 1626, NoDeepContinuationGate, 1862, 1889, 1191, 702,
            796, 1057, 1470, 940, 65, 1147
        };

        public static void Mux(string h264Path, AtracReader atrac, string outputPath) => Mux(h264Path, new[] { atrac }, outputPath);

        public static void Mux(string h264Path, AtracReader[] tracks, string outputPath)
        {
            if (tracks == null || tracks.Length == 0)
                throw new ArgumentException("At least one audio track is required.");
            if (tracks.Length > MaxAudioTracks)
                throw new ArgumentException($"Maximum {MaxAudioTracks} audio tracks supported; got {tracks.Length}.");

            int numTracks = tracks.Length;

            byte[] annexBBytes = File.ReadAllBytes(h264Path);
            List<byte[]> videoUnits = SplitFrames(annexBBytes);
            if (videoUnits.Count == 0)
                throw new InvalidOperationException("No H.264 frames found.");

            List<VideoAccessUnitIndexEntry> videoIndex = ElementaryStreamIndex.BuildVideoFromAnnexB(videoUnits);

            int[] frameSizes = new int[videoUnits.Count];
            long[] frameByteStart = new long[videoUnits.Count];
            long videoByteOffset = 0;
            for (int i = 0; i < videoUnits.Count; i++)
            {
                frameSizes[i] = videoIndex[i].Size;
                frameByteStart[i] = videoByteOffset;
                videoByteOffset += videoUnits[i].Length;
            }

            long totalVideoBytes = videoByteOffset;

            byte[] videoPayload = new byte[totalVideoBytes];
            long videoWriteOffset = 0;
            foreach (byte[] unit in videoUnits)
            {
                Buffer.BlockCopy(unit, 0, videoPayload, (int)videoWriteOffset, unit.Length);
                videoWriteOffset += unit.Length;
            }

            byte[][] audioPayloads = new byte[numTracks][];
            int[] audioBlockAligns = new int[numTracks];
            int[] audioPacketSizes = new int[numTracks];
            long[] audioCursors = new long[numTracks];
            int[] audioPackIndexes = new int[numTracks];
            int[] audioUnitsConsumed = new int[numTracks];
            bool[] audioFirstPack = new bool[numTracks];
            List<AudioAccessUnitIndexEntry>[] audioIndexes = new List<AudioAccessUnitIndexEntry>[numTracks];

            for (int t = 0; t < numTracks; t++)
            {
                audioPayloads[t] = tracks[t].Data;
                audioBlockAligns[t] = tracks[t].BlockAlign;
                audioPacketSizes[t] = tracks[t].PacketSize;
                int audioPacketSize = audioPacketSizes[t];
                audioCursors[t] = 0;
                audioPackIndexes[t] = 0;
                audioUnitsConsumed[t] = 0;
                audioFirstPack[t] = true;
                audioIndexes[t] = ElementaryStreamIndex.BuildAudioFromAtx(tracks[t].FrameCount, audioPacketSize, tracks[t].SampleRate);
            }

            var bfFrameStarts = new List<int>();
            var bfSegments = BuildBfSegments(videoIndex, frameSizes, bfFrameStarts);
            var bfByteBounds = new List<long>();
            long bfByteEnd = 0;
            foreach (int[] segment in bfSegments)
            {
                foreach (int frameSize in segment)
                    bfByteEnd += frameSize;
                bfByteBounds.Add(bfByteEnd);
            }

            bool initialStartupMode = IsInitialStartupMode(bfSegments);

            ulong packDur = PackDuration27M();
            ulong scr27 = ComputeInitialScr(initialStartupMode);
            ulong packDurBase = PackDurationNum / PackDurationDen;
            ulong packDurRem = PackDurationNum % PackDurationDen;
            ulong packDurCarry = initialStartupMode ? PackDurationStartupSeedRem : PackDurationSeedRem;
            bool traceEnabled = string.Equals(Environment.GetEnvironmentVariable("OMPS_TRACE"), "1", StringComparison.Ordinal);
            int packCounter = 0;

            void Trace(string message)
            {
                if (traceEnabled)
                    Console.Error.WriteLine(message);
            }

            void AdvanceScr()
            {
                packDurCarry += packDurRem;
                scr27 += packDurBase + (packDurCarry / PackDurationDen);
                packDurCarry %= PackDurationDen;
            }
            void WaitScr(ulong target)
            {
                if (target <= scr27)
                    return;

                while (true)
                {
                    ulong nextCarry = packDurCarry + packDurRem;
                    ulong nextScr = scr27 + packDurBase + (nextCarry / PackDurationDen);
                    ulong nextCarryNorm = nextCarry % PackDurationDen;

                    if (target <= nextScr)
                    {
                        scr27 = nextScr;
                        packDurCarry = nextCarryNorm;
                        return;
                    }

                    scr27 = nextScr;
                    packDurCarry = nextCarryNorm;
                }
            }

            ulong SnapWaitTarget(ulong target)
            {
                if (target <= scr27)
                    return scr27;

                ulong probeScr = scr27;
                ulong probeCarry = packDurCarry;
                while (true)
                {
                    ulong nextCarry = probeCarry + packDurRem;
                    ulong nextScr = probeScr + packDurBase + (nextCarry / PackDurationDen);
                    ulong nextCarryNorm = nextCarry % PackDurationDen;

                    if (target <= nextScr)
                        return nextScr;

                    probeScr = nextScr;
                    probeCarry = nextCarryNorm;
                }
            }

            byte[] EmitPack(List<byte[]> packets)
            {
                byte[] packHeader = BuildPackHeader(scr27 / 300, scr27 % 300);
                int used = packHeader.Length;
                foreach (byte[] packet in packets) used += packet.Length;
                int paddingSize = PackSize - used;
                byte[] pack = new byte[PackSize];
                int writeOffset = 0;
                Buffer.BlockCopy(packHeader, 0, pack, writeOffset, packHeader.Length); writeOffset += packHeader.Length;
                foreach (byte[] packet in packets) { Buffer.BlockCopy(packet, 0, pack, writeOffset, packet.Length); writeOffset += packet.Length; }
                if (paddingSize >= 6) { byte[] paddingPacket = BuildPadding(paddingSize); Buffer.BlockCopy(paddingPacket, 0, pack, writeOffset, paddingPacket.Length); }
                packCounter++;
                AdvanceScr();
                return pack;
            }

            ulong AudioScrTarget(int t)
            {
                if (audioCursors[t] >= audioPayloads[t].Length) return scr27 + packDur * 1_000_000;
                int payloadLimit = audioFirstPack[t] ? AudioPayloadFirst : AudioPayloadNorm;
                int payloadSize = (int)Math.Min(payloadLimit, audioPayloads[t].Length - audioCursors[t]);
                ushort auPointer = AtxExt16(audioCursors[t], audioBlockAligns[t]);
                int unitStarts = CountAudioUnitStarts(audioCursors[t], payloadSize, audioPacketSizes[t]);
                ulong lead = AudioLeadForPack(audioPackIndexes[t], unitStarts, auPointer);
                if (audioPackIndexes[t] > 1 && payloadSize < payloadLimit && unitStarts < 2)
                    lead += ScrLeadAudioNorm - ScrLeadAudioShort;
                int audioIndex = audioUnitsConsumed[t] < audioIndexes[t].Count ? audioUnitsConsumed[t] : audioIndexes[t].Count - 1;
                if (audioIndex < 0) audioIndex = 0;
                ulong tick240k = audioIndexes[t][audioIndex].EndTick240K;
                ulong pts27Mx2 = PtsAudioStart * 600UL + tick240k * 225UL;
                ulong pts27M = (pts27Mx2 + 1UL) / 2UL;
                ulong target = pts27M > lead ? pts27M - lead : 0;
                if (audioPackIndexes[t] > 1 && unitStarts >= 3 && auPointer <= 175 && target >= 42)
                    target -= 42;
                return target;
            }

            bool AudioHasThreeUnitStarts(int t)
            {
                if (audioCursors[t] >= audioPayloads[t].Length)
                    return false;

                int payloadLimit = audioFirstPack[t] ? AudioPayloadFirst : AudioPayloadNorm;
                int payloadSize = (int)Math.Min(payloadLimit, audioPayloads[t].Length - audioCursors[t]);
                int unitStarts = CountAudioUnitStarts(audioCursors[t], payloadSize, audioPacketSizes[t]);
                return unitStarts >= 3;
            }

            byte[] EmitAudioPack(int t)
            {
                int payloadLimit = audioFirstPack[t] ? AudioPayloadFirst : AudioPayloadNorm;
                int payloadSize = (int)Math.Min(payloadLimit, audioPayloads[t].Length - audioCursors[t]);
                if (payloadSize <= 0) return null;

                byte[] payload = new byte[payloadSize];
                Buffer.BlockCopy(audioPayloads[t], (int)audioCursors[t], payload, 0, payloadSize);

                ulong pts = AudioPtsFromAccessUnitIndex(audioIndexes[t], audioUnitsConsumed[t]);
                ushort auPointer = AtxExt16(audioCursors[t], audioBlockAligns[t]);
                int unitStarts = CountAudioUnitStarts(audioCursors[t], payloadSize, audioPacketSizes[t]);

                byte streamIndex = (byte)t;

                byte[] pes = audioFirstPack[t] ? BuildAudioPesFirst(payload, pts, auPointer, streamIndex) : BuildAudioPes(payload, pts, auPointer, streamIndex);

                audioFirstPack[t] = false;
                audioCursors[t] += payloadSize;
                audioPackIndexes[t] += 1;
                audioUnitsConsumed[t] += unitStarts;

                byte[] packHeader = BuildPackHeader(scr27 / 300, scr27 % 300);
                byte[] pack = new byte[PackSize];
                int writeOffset = 0;
                Buffer.BlockCopy(packHeader, 0, pack, writeOffset, packHeader.Length); writeOffset += packHeader.Length;
                Buffer.BlockCopy(pes, 0, pack, writeOffset, pes.Length); writeOffset += pes.Length;
                int paddingSize = PackSize - writeOffset;
                if (paddingSize >= 6) { byte[] paddingPacket = BuildPadding(paddingSize); Buffer.BlockCopy(paddingPacket, 0, pack, writeOffset, paddingPacket.Length); }
                packCounter++;
                AdvanceScr();
                return pack;
            }

            bool AnyAudioRemaining() { for (int t = 0; t < numTracks; t++) if (audioCursors[t] < audioPayloads[t].Length) return true; return false; }

            var packs = new List<byte[]>();

            int bfIndex = 0;
            long nextBfThreshold = bfByteBounds.Count > 0 ? bfByteBounds[0] : totalVideoBytes + 1;

            if (bfSegments.Count > 0)
            {
                byte[] sysHdr = BuildSystemHeader();
                byte[] bf0 = BuildBfPes(bfSegments[0]);
                int fixed0 = 14 + sysHdr.Length + bf0.Length + (6 + 3 + 13);
                int tsStep0 = TimestampStepForSegment(0, bfFrameStarts.Count, initialStartupMode);
                int nextPtsFrame0 = Math.Min(bfFrameStarts[0] + tsStep0, frameByteStart.Length);
                long nextTsByte0 = nextPtsFrame0 < frameByteStart.Length ? frameByteStart[nextPtsFrame0] : totalVideoBytes;
                int chunk0Len = (int)Math.Min(Math.Max(0, PackSize - fixed0), nextTsByte0);
                chunk0Len = (int)Math.Min(chunk0Len, totalVideoBytes);
                byte[] chunk0 = new byte[chunk0Len];
                Buffer.BlockCopy(videoPayload, 0, chunk0, 0, chunk0Len);
                long vCursorLocal = chunk0Len;

                (ulong pts0, ulong dts0) = VideoPtsDts(0);
                Trace($"pack {packCounter} init seg scr={scr27} pts={pts0} dts={dts0} bytes={chunk0Len}");
                packs.Add(EmitPack(new List<byte[]> { sysHdr, bf0, BuildVideoSegPes(chunk0, pts0, dts0) }));
                bfIndex = 1;

                RunMuxLoop(videoPayload, totalVideoBytes, frameByteStart, bfSegments, bfFrameStarts, bfByteBounds, numTracks, audioPayloads, audioCursors, audioFirstPack, packs, ref scr27, packDur, vCursorLocal, bfIndex, nextBfThreshold, nextPtsFrame0, 0, initialStartupMode, WaitScr, SnapWaitTarget, EmitPack, EmitAudioPack, AudioScrTarget, AudioHasThreeUnitStarts, AnyAudioRemaining, Trace, () => packCounter);
            }

            PatchBfRpiFields(packs, bfFrameStarts, frameByteStart);

            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                foreach (byte[] pk in packs)
                    fs.Write(pk, 0, pk.Length);
        }

        private static void RunMuxLoop(byte[] videoPayload, long totalVideoBytes, long[] frameByteStart, List<int[]> bfSegments, List<int> bfFrameStarts, List<long> bfByteBounds, int numTracks, byte[][] audioPayloads, long[] audioCursors, bool[] audioFirstPack, List<byte[]> packs, ref ulong scr27, ulong packDur, long vCursorInit, int bfIndexInit, long nextBfThresholdInit, int nextPtsFrameInit, int currentSegmentIndexInit, bool initialStartupMode, Action<ulong> waitScr, Func<ulong, ulong> snapWaitTarget, Func<List<byte[]>, byte[]> emitPack, Func<int, byte[]> emitAudioPack, Func<int, ulong> audioScrTarget, Func<int, bool> audioHasThreeUnitStarts, Func<bool> anyAudioRemaining, Action<string> trace, Func<int> currentPack)
        {
            long vCursor = vCursorInit;
            int bfIndex = bfIndexInit;
            long nextBfThreshold = nextBfThresholdInit;
            int nextPtsFrame = nextPtsFrameInit;
            int nextAudioTrack = 0;
            int currentSegmentIndex = currentSegmentIndexInit;
            int currentTsStep = TimestampStepForSegment(currentSegmentIndex, bfFrameStarts.Count, initialStartupMode);
            while (vCursor < totalVideoBytes || anyAudioRemaining())
            {
                int currentSegmentEndFrame = SegmentEndFrame(bfFrameStarts, currentSegmentIndex, frameByteStart.Length);
                while (nextPtsFrame < currentSegmentEndFrame && nextPtsFrame < frameByteStart.Length && vCursor > frameByteStart[nextPtsFrame])
                    nextPtsFrame++;

                while (true)
                {
                    if (bfIndex < bfSegments.Count && vCursor >= nextBfThreshold)
                        break;

                    bool audioEmitted = false;
                    bool hasPendingAudio = TryGetNextAudioTarget(numTracks, audioPayloads, audioCursors, audioScrTarget, audioHasThreeUnitStarts, out ulong pendingAudioTarget, out bool pendingAudioHasThreeUnitStarts);
                    bool reserveForTimestamp = ShouldReserveVideoBeforeTimestamp(vCursor, totalVideoBytes, frameByteStart, bfFrameStarts, nextPtsFrame, currentSegmentIndex, currentTsStep, scr27, packDur, initialStartupMode);
                    bool reserveForBf = ShouldReserveVideoBeforeBf(vCursor, nextBfThreshold, bfIndex, bfFrameStarts, nextPtsFrame, currentTsStep, scr27, packDur, initialStartupMode);
                    bool reserveForVideo = reserveForTimestamp || reserveForBf;

                    bool beforeBfBoundary = bfIndex >= bfSegments.Count || vCursor < nextBfThreshold;

                    if (beforeBfBoundary &&
                        reserveForTimestamp &&
                        !reserveForBf &&
                        hasPendingAudio &&
                        pendingAudioTarget > scr27 &&
                        ShouldWaitForAudioBeforeVideo(vCursor, totalVideoBytes, frameByteStart, bfFrameStarts, nextBfThreshold, bfIndex, nextPtsFrame, currentSegmentIndex, currentTsStep, scr27, pendingAudioTarget, pendingAudioHasThreeUnitStarts, initialStartupMode, snapWaitTarget))
                    {
                        trace($"wait audio target={pendingAudioTarget} from scr={scr27}");
                        waitScr(pendingAudioTarget);
                        continue;
                    }

                    if (beforeBfBoundary && reserveForTimestamp && !reserveForBf && hasPendingAudio && pendingAudioTarget <= scr27)
                        reserveForVideo = false;

                    if (beforeBfBoundary &&
                        reserveForBf &&
                        !reserveForTimestamp &&
                        hasPendingAudio &&
                        pendingAudioTarget <= scr27 &&
                        bfIndex < bfSegments.Count &&
                        pendingAudioTarget <= BfTargetForSegment(bfFrameStarts, bfIndex, initialStartupMode))
                    {
                        reserveForVideo = false;
                    }

                    if (reserveForVideo)
                        break;

                    int roundStart = nextAudioTrack;
                    ulong nextAudioTarget = ulong.MaxValue;
                    bool nextAudioHasThreeUnitStarts = false;
                    for (int offset = 0; offset < numTracks; offset++)
                    {
                        int t = (roundStart + offset) % numTracks;
                        if (audioCursors[t] >= audioPayloads[t].Length)
                            continue;

                        ulong audioTarget = audioScrTarget(t);
                        bool audioReady = scr27 >= audioTarget;
                        if (audioTarget < nextAudioTarget)
                        {
                            nextAudioTarget = audioTarget;
                            nextAudioHasThreeUnitStarts = audioHasThreeUnitStarts(t);
                        }
                        else if (audioTarget == nextAudioTarget)
                        {
                            nextAudioHasThreeUnitStarts |= audioHasThreeUnitStarts(t);
                        }

                        if (!audioReady)
                            continue;

                        trace($"pack {currentPack()} audio t={t} scr={scr27} target={audioScrTarget(t)} cursor={audioCursors[t]}");
                        byte[] ap = emitAudioPack(t);
                        if (ap == null)
                            continue;

                        packs.Add(ap);
                        nextAudioTrack = (t + 1) % numTracks;
                        audioEmitted = true;
                    }

                    if (audioEmitted)
                        continue;

                    if (nextAudioTarget != ulong.MaxValue &&
                        nextAudioTarget > scr27 &&
                        ShouldWaitForAudioBeforeVideo(vCursor, totalVideoBytes, frameByteStart, bfFrameStarts, nextBfThreshold, bfIndex, nextPtsFrame, currentSegmentIndex, currentTsStep, scr27, nextAudioTarget, nextAudioHasThreeUnitStarts, initialStartupMode, snapWaitTarget))
                    {
                        trace($"wait audio target={nextAudioTarget} from scr={scr27}");
                        waitScr(nextAudioTarget);
                        continue;
                    }

                    break;
                }

                if (bfIndex < bfSegments.Count && vCursor >= nextBfThreshold)
                {
                    ulong bfTarget = BfTargetForSegment(bfFrameStarts, bfIndex, initialStartupMode);
                    ulong bfDrainTarget = bfTarget;
                    ulong bfWaitTarget = bfTarget;
                    bool usePenultimateTimestampGate = TryGetPenultimateBfTimestampGate(frameByteStart, bfFrameStarts, bfIndex, initialStartupMode, out ulong penultimateTimestampGate);
                    if (usePenultimateTimestampGate)
                    {
                        bfDrainTarget = penultimateTimestampGate;
                        bfWaitTarget = penultimateTimestampGate;
                    }
                    ulong finalTimestampGate = 0;
                    bool useFinalTimestampGate = !usePenultimateTimestampGate && TryGetFinalBfTimestampGate(frameByteStart, bfFrameStarts, bfIndex, out finalTimestampGate);
                    if (useFinalTimestampGate)
                    {
                        bfDrainTarget = finalTimestampGate;
                        bfWaitTarget = finalTimestampGate;
                    }

                    if (TryGetNextAudioTarget(numTracks, audioPayloads, audioCursors, audioScrTarget, out ulong nextAudioTarget) &&
                        ShouldDrainAudioBeforeBf(bfDrainTarget, nextAudioTarget, packDur))
                    {
                        if (nextAudioTarget > scr27)
                        {
                            if (nextAudioTarget <= bfDrainTarget)
                            {
                                trace($"wait audio-before-bf target={nextAudioTarget} bfTarget={bfDrainTarget} from scr={scr27}");
                                waitScr(nextAudioTarget);
                                continue;
                            }
                        }
                        else
                        {
                            bool drainedAudio = false;
                            int roundStart = nextAudioTrack;
                            for (int offset = 0; offset < numTracks; offset++)
                            {
                                int t = (roundStart + offset) % numTracks;
                                if (audioCursors[t] >= audioPayloads[t].Length || audioScrTarget(t) > scr27)
                                    continue;

                                trace($"pack {currentPack()} audio t={t} scr={scr27} target={audioScrTarget(t)} cursor={audioCursors[t]}");
                                byte[] ap = emitAudioPack(t);
                                if (ap == null)
                                    continue;

                                packs.Add(ap);
                                nextAudioTrack = (t + 1) % numTracks;
                                drainedAudio = true;
                            }

                            if (drainedAudio)
                                continue;
                        }
                    }

                    byte[] sysHdr2 = BuildSystemHeader();
                    byte[] bf = BuildBfPes(bfSegments[bfIndex]);
                    nextBfThreshold = bfIndex < bfByteBounds.Count ? bfByteBounds[bfIndex] : totalVideoBytes + 1;
                    bfIndex++;

                    if (vCursor < totalVideoBytes)
                    {
                        int fi2 = FrameAt(frameByteStart, vCursor);
                        (ulong ptsV, ulong dtsV) = VideoPtsDts(fi2);
                        currentSegmentIndex = bfIndex - 1;
                        currentTsStep = TimestampStepForSegment(currentSegmentIndex, bfFrameStarts.Count, initialStartupMode);
                        if (usePenultimateTimestampGate)
                            currentTsStep = PtsStampIntervalFinal;
                        nextPtsFrame = Math.Min(bfFrameStarts[currentSegmentIndex] + currentTsStep, frameByteStart.Length);
                        trace($"wait BF seg target={bfWaitTarget} from scr={scr27}");
                        ulong bfWaitTolerance = useFinalTimestampGate ? 0 : VideoBfWaitTolerance27M;
                        waitScr(bfWaitTarget > bfWaitTolerance ? bfWaitTarget - bfWaitTolerance : 0);
                        int fixedB = 14 + sysHdr2.Length + bf.Length + (6 + 3 + 13);
                        int availB = (int)Math.Min(Math.Max(0, PackSize - fixedB), nextBfThreshold - vCursor);
                        long nextTsByte = nextPtsFrame < frameByteStart.Length ? frameByteStart[nextPtsFrame] : totalVideoBytes;
                        availB = (int)Math.Min(availB, Math.Max(0, nextTsByte - vCursor));
                        availB = (int)Math.Min(availB, totalVideoBytes - vCursor);
                        byte[] chunkB = new byte[availB];
                        Buffer.BlockCopy(videoPayload, (int)vCursor, chunkB, 0, availB);
                        vCursor += availB;
                        for (int t = 0; t < numTracks; t++) audioFirstPack[t] = true;
                        trace($"pack {currentPack()} bf seg idx={currentSegmentIndex} scr={scr27} pts={ptsV} dts={dtsV} bytes={availB}");
                        packs.Add(emitPack(new List<byte[]> { sysHdr2, bf, BuildVideoSegPes(chunkB, ptsV, dtsV) }));
                    }

                    else
                    {
                        for (int t = 0; t < numTracks; t++) audioFirstPack[t] = true;
                        trace($"pack {currentPack()} bf-only idx={bfIndex - 1} scr={scr27}");
                        packs.Add(emitPack(new List<byte[]> { sysHdr2, bf }));
                    }
                    continue;
                }

                if (vCursor < totalVideoBytes)
                {
                    int currentSegmentEndFrameForTs = SegmentEndFrame(bfFrameStarts, currentSegmentIndex, frameByteStart.Length);
                    bool canEmitTimestampInCurrentSegment = nextPtsFrame < currentSegmentEndFrameForTs;
                    long tsBound = nextPtsFrame < frameByteStart.Length ? frameByteStart[nextPtsFrame] : totalVideoBytes + 1;
                    int tsPayload = VideoPayload - 10;
                    if (bfIndex < bfSegments.Count) tsPayload = (int)Math.Min(tsPayload, nextBfThreshold - vCursor);
                    tsPayload = (int)Math.Min(tsPayload, totalVideoBytes - vCursor);

                    if (canEmitTimestampInCurrentSegment && tsPayload > 0 && vCursor <= tsBound && tsBound < vCursor + tsPayload)
                    {
                        int stampedFrame = nextPtsFrame;
                        int followingPtsFrame = stampedFrame + currentTsStep;
                        long nextChunkBoundary = followingPtsFrame < frameByteStart.Length ? frameByteStart[followingPtsFrame] : totalVideoBytes;
                        tsPayload = (int)Math.Min(tsPayload, Math.Max(0, nextChunkBoundary - vCursor));

                        (ulong ptsV, ulong dtsV) = VideoPtsDts(stampedFrame);
                        nextPtsFrame = followingPtsFrame;
                        int segmentStartFrame = SegmentStartFrame(bfFrameStarts, currentSegmentIndex);
                        int segmentEndFrame = SegmentEndFrame(bfFrameStarts, currentSegmentIndex, frameByteStart.Length);
                        bool isTailTs = followingPtsFrame >= segmentEndFrame;
                        ulong tsLead = VideoTsLeadForSegment(currentSegmentIndex, bfFrameStarts.Count, segmentStartFrame, stampedFrame, isTailTs, initialStartupMode);
                        ulong tsTarget = dtsV * 300 > tsLead ? dtsV * 300 - tsLead : 0;
                        ulong tsWaitTarget = EffectiveTimestampWaitTarget(bfFrameStarts, frameByteStart.Length, currentSegmentIndex, currentTsStep, stampedFrame, followingPtsFrame, tsTarget, scr27);
                        trace($"wait TS target={tsWaitTarget} from scr={scr27}");
                        waitScr(tsWaitTarget);
                        byte[] chunkTs = new byte[tsPayload];
                        Buffer.BlockCopy(videoPayload, (int)vCursor, chunkTs, 0, tsPayload);
                        vCursor += tsPayload;
                        trace($"pack {currentPack()} ts scr={scr27} frame={stampedFrame} pts={ptsV} dts={dtsV} bytes={tsPayload}");
                        packs.Add(emitPack(new List<byte[]> { BuildVideoTsPes(chunkTs, ptsV, dtsV) }));
                        continue;
                    }
                }

                if (vCursor < totalVideoBytes)
                {
                    int len3 = GetPlainPayloadLength(vCursor, totalVideoBytes, bfIndex, nextBfThreshold, bfFrameStarts);
                    bool isTailBf = bfIndex < bfSegments.Count && vCursor + len3 >= nextBfThreshold && len3 < VideoPayload;
                    bool midFrameContinuation = IsMidFrameContinuation(frameByteStart, totalVideoBytes, vCursor, len3);

                    bool waitedForDeepContinuation = false;
                    if (len3 > 0 &&
                        IsDeepContinuationRegion(bfFrameStarts, currentSegmentIndex) &&
                        TryGetDeepContinuationTarget(frameByteStart, totalVideoBytes, vCursor, len3, SegmentStartFrame(bfFrameStarts, currentSegmentIndex), scr27, priorityMode: false, out ulong deepTarget, out int maxBytesToTarget, out long deepBytesToTarget) &&
                        (!TryGetNextAudioTarget(numTracks, audioPayloads, audioCursors, audioScrTarget, out ulong nextAudioTarget) || nextAudioTarget >= deepTarget))
                    {
                        trace($"wait deep-cont target={deepTarget} from scr={scr27}");
                        if (maxBytesToTarget > VideoPayload && deepBytesToTarget > VideoPayload)
                        {
                            ulong deepTolerance = DeepContinuationTolerance27M;
                            waitScr(deepTarget > deepTolerance ? deepTarget - deepTolerance : 0);
                        }
                        else
                        {
                            waitScr(deepTarget);
                        }
                        waitedForDeepContinuation = true;
                    }

                    if (len3 > 0 && !waitedForDeepContinuation && (!midFrameContinuation || isTailBf))
                    {
                        ulong plainTarget = ComputePlainVideoTarget(frameByteStart, totalVideoBytes, vCursor, len3, bfIndex, nextBfThreshold, bfFrameStarts);
                        trace($"wait plain target={plainTarget} from scr={scr27}");
                        waitScr(plainTarget);
                    }

                    byte[] chunkV = new byte[len3];
                    Buffer.BlockCopy(videoPayload, (int)vCursor, chunkV, 0, len3);
                    vCursor += len3;
                    trace($"pack {currentPack()} plain scr={scr27} bytes={len3} vCursor={vCursor}");
                    packs.Add(emitPack(new List<byte[]> { BuildVideoNoTsPes(chunkV) }));
                }

                else if (anyAudioRemaining())
                {
                    for (int offset = 0; offset < numTracks; offset++)
                    {
                        int t = (nextAudioTrack + offset) % numTracks;
                        if (audioCursors[t] < audioPayloads[t].Length)
                        {
                            trace($"pack {currentPack()} drain-audio t={t} scr={scr27} target={audioScrTarget(t)} cursor={audioCursors[t]}");
                            byte[] ap2 = emitAudioPack(t);
                            if (ap2 != null)
                            {
                                packs.Add(ap2);
                                nextAudioTrack = (t + 1) % numTracks;
                            }
                            break;
                        }
                    }
                }
                else break;
            }
        }

        private static void PatchBfRpiFields(List<byte[]> packs, List<int> bfFrameStarts, long[] frameByteStart)
        {
            if (packs == null || packs.Count == 0 || bfFrameStarts == null || bfFrameStarts.Count == 0 || frameByteStart == null)
                return;

            int[] framePack = new int[frameByteStart.Length];
            for (int i = 0; i < framePack.Length; i++)
                framePack[i] = -1;

            long videoCursor = 0;
            int nextFrame = 0;

            for (int packIndex = 0; packIndex < packs.Count; packIndex++)
            {
                byte[] pack = packs[packIndex];
                int pos = 14;

                while (pos + 6 <= pack.Length && IsStartCode(pack, pos))
                {
                    byte sid = pack[pos + 3];
                    int len = (pack[pos + 4] << 8) | pack[pos + 5];
                    int next = pos + 6 + len;

                    if (sid == SidVideo && pos + 9 <= pack.Length)
                    {
                        int headerLen = pack[pos + 8];
                        int payloadLen = len - 3 - headerLen;
                        if (payloadLen > 0)
                        {
                            long payloadStart = videoCursor;
                            long payloadEnd = videoCursor + payloadLen;

                            while (nextFrame < frameByteStart.Length && frameByteStart[nextFrame] < payloadEnd)
                            {
                                if (frameByteStart[nextFrame] >= payloadStart)
                                    framePack[nextFrame] = packIndex;

                                nextFrame++;
                            }

                            videoCursor = payloadEnd;
                        }
                    }

                    if (sid == SidPadding)
                        break;

                    pos = next;
                }
            }

            int bfRecordIndex = 0;
            for (int packIndex = 0; packIndex < packs.Count && bfRecordIndex < bfFrameStarts.Count; packIndex++)
            {
                byte[] pack = packs[packIndex];
                int pos = 14;

                while (pos + 6 <= pack.Length && IsStartCode(pack, pos))
                {
                    byte sid = pack[pos + 3];
                    int len = (pack[pos + 4] << 8) | pack[pos + 5];

                    if (sid == SidPriv2)
                    {
                        int payload = pos + 6;
                        int segmentStart = bfFrameStarts[bfRecordIndex++];

                        for (int i = 0; i < 4; i++)
                        {
                            int frame = segmentStart + i + 1;
                            int relativePack = 0;
                            if (frame < framePack.Length && framePack[frame] >= packIndex)
                                relativePack = framePack[frame] - packIndex;

                            int field = payload + 2 + i * 2;
                            pack[field] = (byte)((relativePack >> 8) & 0xFF);
                            pack[field + 1] = (byte)(relativePack & 0xFF);
                        }

                        break;
                    }

                    if (sid == SidPadding)
                        break;

                    pos += 6 + len;
                }
            }
        }

        private static bool IsStartCode(byte[] data, int offset)
        {
            return offset + 3 < data.Length && data[offset] == 0x00 && data[offset + 1] == 0x00 && data[offset + 2] == 0x01;
        }

        private static bool IsInitialStartupMode(List<int[]> bfSegments)
        {
            if (bfSegments == null || bfSegments.Count == 0 || bfSegments[0] == null)
                return false;

            int total = 0;
            for (int i = 0; i < bfSegments[0].Length; i++)
                total += bfSegments[0][i];

            return total <= VideoPayload * 2;
        }

        private static List<byte[]> SplitFrames(byte[] data)
        {
            var sc = new List<(int pos, int len)>();
            for (int i = 0; i < data.Length - 2; i++)
            {
                if (data[i] == 0 && data[i + 1] == 0)
                {
                    if (i + 3 < data.Length && data[i + 2] == 0 && data[i + 3] == 1) { sc.Add((i, 4)); i += 3; }
                    else if (data[i + 2] == 1) { sc.Add((i, 3)); i += 2; }
                }
            }

            var nals = new List<(byte[] data, int pfx)>();
            for (int i = 0; i < sc.Count; i++)
            {
                int s = sc[i].pos;
                int e = i + 1 < sc.Count ? sc[i + 1].pos : data.Length;
                if (e <= s) continue;
                byte[] n = new byte[e - s];
                Buffer.BlockCopy(data, s, n, 0, n.Length);
                nals.Add((n, sc[i].len));
            }

            var aus = new List<List<byte[]>>();
            var pend = new List<byte[]>();
            foreach ((byte[] n, int pfx) in nals)
            {
                int t = n.Length > pfx ? n[pfx] & 0x1F : 0;
                if (t == 1 || t == 5)
                {
                    pend.Add(n);
                    aus.Add(new List<byte[]>(pend));
                    pend.Clear();
                }
                else pend.Add(n);
            }

            if (pend.Count > 0 && aus.Count > 0) aus[aus.Count - 1].AddRange(pend);

            var result = new List<byte[]>();
            foreach (var au in aus)
            {
                int total = 0; foreach (byte[] n in au) total += n.Length;
                byte[] buf = new byte[total]; int off = 0;
                foreach (byte[] n in au) { Buffer.BlockCopy(n, 0, buf, off, n.Length); off += n.Length; }
                result.Add(buf);
            }
            return result;
        }

        private static (ulong pts, ulong dts) VideoPtsDts(int frameIndex)
        {
            ulong pts = PtsVideoStart + (ulong)frameIndex * FrameDuration90K;
            ulong dts = pts - FrameDuration90K;
            return (pts, dts);
        }

        private static ulong AudioLeadForPack(int packIndex, int auStarts, ushort ext16)
        {
            if (packIndex == 0) return ScrLeadAudioBoot0;
            if (packIndex == 1) return ScrLeadAudioBoot1;

            return auStarts >= 3 && ext16 <= 175 ? ScrLeadAudioShort : ScrLeadAudioNorm;
        }

        private static int CountAudioUnitStarts(long cursor, int payloadSize, int packetSize)
        {
            if (payloadSize <= 0 || packetSize <= 0)
                return 0;

            long rangeEnd = cursor + payloadSize;
            long first = ((cursor + packetSize - 1) / packetSize) * packetSize;
            int count = 0;

            for (long pos = first; pos < rangeEnd; pos += packetSize)
                count++;

            return count;
        }

        private static bool ShouldReserveVideoBeforeTimestamp(long vCursor, long totalVideoBytes, long[] frameByteStart, List<int> bfFrameStarts, int nextPtsFrame, int currentSegmentIndex, int currentTsStep, ulong scr27, ulong packDur, bool initialStartupMode)
        {
            if (!TryGetNextTimestampTarget(frameByteStart, bfFrameStarts, nextPtsFrame, currentSegmentIndex, currentTsStep, initialStartupMode, out ulong nextVideoTarget))
                return false;

            ulong packsUntilTarget = nextVideoTarget > scr27 ? ((nextVideoTarget - scr27) / packDur) + 1 : 1;
            ulong requiredVideoPacks = RequiredPacksBeforeTimestamp(vCursor, totalVideoBytes, frameByteStart, nextPtsFrame);
            return requiredVideoPacks > packsUntilTarget;
        }

        private static bool ShouldReserveVideoBeforeBf(long vCursor, long nextBfThreshold, int bfIndex, List<int> bfFrameStarts, int nextPtsFrame, int currentTsStep, ulong scr27, ulong packDur, bool initialStartupMode)
        {
            if (bfIndex >= bfFrameStarts.Count || vCursor >= nextBfThreshold)
                return false;

            int nextSyncFrame = bfFrameStarts[bfIndex];
            ulong syncDts = VideoPtsDts(nextSyncFrame).dts;
            ulong segLead = VideoSegLeadForSegment(bfIndex, bfFrameStarts.Count, initialStartupMode);
            ulong bfTarget = syncDts * 300 > segLead ? syncDts * 300 - segLead : 0;

            if (bfTarget <= scr27)
                return true;

            ulong packsUntilDeadline = ((bfTarget - scr27) / packDur) + 1;
            if (packsUntilDeadline == 0)
                return true;

            int remainingTsPacks = CountRemainingTimestampPacks(nextPtsFrame, nextSyncFrame, currentTsStep);
            long remainingBytes = nextBfThreshold - vCursor;
            long residualBytes = remainingBytes - (long)remainingTsPacks * (VideoPayload - 10);
            if (residualBytes < 0)
                residualBytes = 0;

            ulong remainingPlainPacks = (ulong)((residualBytes + VideoPayload - 1) / VideoPayload);
            ulong requiredVideoPacks = (ulong)remainingTsPacks + remainingPlainPacks;
            return requiredVideoPacks >= packsUntilDeadline;
        }

        private static bool ShouldWaitForAudioBeforeVideo(long vCursor, long totalVideoBytes, long[] frameByteStart, List<int> bfFrameStarts, long nextBfThreshold, int bfIndex, int nextPtsFrame, int currentSegmentIndex, int currentTsStep, ulong scr27, ulong nextAudioTarget, bool nextAudioHasThreeUnitStarts, bool initialStartupMode, Func<ulong, ulong> snapWaitTarget)
        {
            if (!TryGetNextVideoActionTarget(vCursor, totalVideoBytes, frameByteStart, bfFrameStarts, nextBfThreshold, bfIndex, nextPtsFrame, currentSegmentIndex, currentTsStep, scr27, initialStartupMode, out ulong nextVideoTarget))
                return true;

            ulong audioSnap = snapWaitTarget(nextAudioTarget);
            ulong videoSnap = snapWaitTarget(nextVideoTarget);
            if (audioSnap != videoSnap)
                return audioSnap < videoSnap;

            if (!nextAudioHasThreeUnitStarts &&
                nextAudioTarget > nextVideoTarget &&
                nextAudioTarget - nextVideoTarget <= AudioPlainRaceTolerance27M * 2 &&
                audioSnap - nextAudioTarget <= AudioPlainRaceTolerance27M)
                return false;

            return true;
        }

        private static bool TryGetNextAudioTarget(int numTracks, byte[][] audioPayloads, long[] audioCursors, Func<int, ulong> audioScrTarget, out ulong nextAudioTarget)
        {
            nextAudioTarget = ulong.MaxValue;
            for (int t = 0; t < numTracks; t++)
            {
                if (audioCursors[t] >= audioPayloads[t].Length)
                    continue;

                nextAudioTarget = Math.Min(nextAudioTarget, audioScrTarget(t));
            }

            return nextAudioTarget != ulong.MaxValue;
        }

        private static bool TryGetNextAudioTarget(int numTracks, byte[][] audioPayloads, long[] audioCursors, Func<int, ulong> audioScrTarget, Func<int, bool> audioHasThreeUnitStarts, out ulong nextAudioTarget, out bool nextAudioHasThreeUnitStarts)
        {
            nextAudioTarget = ulong.MaxValue;
            nextAudioHasThreeUnitStarts = false;

            for (int t = 0; t < numTracks; t++)
            {
                if (audioCursors[t] >= audioPayloads[t].Length)
                    continue;

                ulong target = audioScrTarget(t);
                if (target < nextAudioTarget)
                {
                    nextAudioTarget = target;
                    nextAudioHasThreeUnitStarts = audioHasThreeUnitStarts(t);
                }
                else if (target == nextAudioTarget)
                {
                    nextAudioHasThreeUnitStarts |= audioHasThreeUnitStarts(t);
                }
            }

            return nextAudioTarget != ulong.MaxValue;
        }

        private static bool ShouldDrainAudioBeforeBf(ulong bfTarget, ulong nextAudioTarget, ulong packDur)
        {
            return nextAudioTarget <= bfTarget + packDur;
        }

        private static bool TryGetPenultimateBfTimestampGate(long[] frameByteStart, List<int> bfFrameStarts, int bfIndex, bool initialStartupMode, out ulong timestampGate)
        {
            timestampGate = 0;
            if (frameByteStart == null ||
                bfFrameStarts == null ||
                bfIndex >= bfFrameStarts.Count - 1 ||
                !IsLateTimestampGateRegion(bfFrameStarts[bfIndex]) ||
                !IsNearStreamEnd(frameByteStart.Length, bfFrameStarts[bfIndex]))
            {
                return false;
            }

            int tsStep = PtsStampIntervalFinal;
            int nextPtsFrame = Math.Min(bfFrameStarts[bfIndex] + tsStep, frameByteStart.Length);
            return TryGetNextTimestampTarget(frameByteStart, bfFrameStarts, nextPtsFrame, bfIndex, tsStep, initialStartupMode, out timestampGate);
        }

        private static bool TryGetFinalBfTimestampGate(long[] frameByteStart, List<int> bfFrameStarts, int bfIndex, out ulong timestampGate)
        {
            timestampGate = 0;
            if (frameByteStart == null ||
                bfFrameStarts == null ||
                bfIndex != bfFrameStarts.Count - 1 ||
                !IsLateTimestampGateRegion(bfFrameStarts[bfIndex]) ||
                frameByteStart.Length == 0)
            {
                return false;
            }

            int lastFrame = frameByteStart.Length - 1;
            ulong dts = VideoPtsDts(lastFrame).dts;
            timestampGate = dts * 300 > ScrLeadVideoMid ? dts * 300 - ScrLeadVideoMid : 0;
            return true;
        }

        private static bool TryGetNextTimestampTarget(long[] frameByteStart, List<int> bfFrameStarts, int nextPtsFrame, int currentSegmentIndex, int currentTsStep, bool initialStartupMode, out ulong nextVideoTarget)
        {
            nextVideoTarget = 0;
            if (frameByteStart == null || nextPtsFrame < 0 || nextPtsFrame >= frameByteStart.Length)
                return false;

            int segmentEndFrame = SegmentEndFrame(bfFrameStarts, currentSegmentIndex, frameByteStart.Length);
            if (nextPtsFrame >= segmentEndFrame)
                return false;

            int segmentStartFrame = SegmentStartFrame(bfFrameStarts, currentSegmentIndex);
            bool isTailTs = nextPtsFrame + currentTsStep >= segmentEndFrame;
            ulong tsLead = VideoTsLeadForSegment(currentSegmentIndex, bfFrameStarts.Count, segmentStartFrame, nextPtsFrame, isTailTs, initialStartupMode);
            ulong dts = VideoPtsDts(nextPtsFrame).dts;
            nextVideoTarget = dts * 300 > tsLead ? dts * 300 - tsLead : 0;
            return true;
        }

        private static ulong EffectiveTimestampWaitTarget(List<int> bfFrameStarts, int totalFrames, int currentSegmentIndex, int currentTsStep, int stampedFrame, int followingPtsFrame, ulong tsTarget, ulong scr27)
        {
            if (bfFrameStarts == null ||
                currentTsStep != PtsStampIntervalFinal ||
                currentSegmentIndex < 0 ||
                currentSegmentIndex >= bfFrameStarts.Count ||
                !IsLateTimestampGateRegion(SegmentStartFrame(bfFrameStarts, currentSegmentIndex)))
            {
                return tsTarget;
            }

            int segmentEndFrame = SegmentEndFrame(bfFrameStarts, currentSegmentIndex, totalFrames);
            if (followingPtsFrame < segmentEndFrame && tsTarget > scr27)
                return tsTarget;

            int waitFrame = followingPtsFrame < segmentEndFrame ? followingPtsFrame : segmentEndFrame;
            if (waitFrame <= stampedFrame || waitFrame >= totalFrames)
                return tsTarget;

            ulong dts = VideoPtsDts(waitFrame).dts;
            return dts * 300 > ScrLeadVideoMid ? dts * 300 - ScrLeadVideoMid : 0;
        }

        private static bool TryGetNextVideoActionTarget(long vCursor, long totalVideoBytes, long[] frameByteStart, List<int> bfFrameStarts, long nextBfThreshold, int bfIndex, int nextPtsFrame, int currentSegmentIndex, int currentTsStep, ulong scr27, bool initialStartupMode, out ulong nextVideoTarget)
        {
            nextVideoTarget = ulong.MaxValue;

            if (bfIndex < bfFrameStarts.Count)
            {
                int nextSyncFrame = bfFrameStarts[bfIndex];
                ulong syncDts = VideoPtsDts(nextSyncFrame).dts;
                ulong segLead = VideoSegLeadForSegment(bfIndex, bfFrameStarts.Count, initialStartupMode);
                ulong bfTarget = syncDts * 300 > segLead ? syncDts * 300 - segLead : 0;
                if (!IsDeepContinuationRegion(bfFrameStarts, currentSegmentIndex) || bfTarget > scr27)
                    nextVideoTarget = Math.Min(nextVideoTarget, bfTarget);
            }

            if (TryGetNextTimestampTarget(frameByteStart, bfFrameStarts, nextPtsFrame, currentSegmentIndex, currentTsStep, initialStartupMode, out ulong tsTarget))
            {
                int followingPtsFrame = nextPtsFrame + currentTsStep;
                ulong effectiveTsTarget = EffectiveTimestampWaitTarget(bfFrameStarts, frameByteStart.Length, currentSegmentIndex, currentTsStep, nextPtsFrame, followingPtsFrame, tsTarget, scr27);
                if (!IsDeepContinuationRegion(bfFrameStarts, currentSegmentIndex) || effectiveTsTarget > scr27)
                    nextVideoTarget = Math.Min(nextVideoTarget, effectiveTsTarget);
            }

            if (TryGetNextPlainVideoTarget(vCursor, totalVideoBytes, frameByteStart, bfIndex, nextBfThreshold, bfFrameStarts, scr27, out ulong plainTarget))
            {
                int plainLen = GetPlainPayloadLength(vCursor, totalVideoBytes, bfIndex, nextBfThreshold, bfFrameStarts);
                if (IsDeepContinuationRegion(bfFrameStarts, currentSegmentIndex) &&
                    plainLen > 0 &&
                    TryGetDeepContinuationTarget(frameByteStart, totalVideoBytes, vCursor, plainLen, SegmentStartFrame(bfFrameStarts, currentSegmentIndex), scr27, priorityMode: true, out ulong deepTarget, out _, out _))
                {
                    plainTarget = deepTarget;
                }

                if (plainTarget < nextVideoTarget)
                    nextVideoTarget = plainTarget;
            }

            return nextVideoTarget != ulong.MaxValue;
        }

        private static int CountRemainingTimestampPacks(int nextPtsFrame, int segmentEndFrame, int currentTsStep)
        {
            if (currentTsStep <= 0 || nextPtsFrame >= segmentEndFrame)
                return 0;

            int count = 0;
            for (int frame = nextPtsFrame; frame < segmentEndFrame; frame += currentTsStep)
                count++;

            return count;
        }

        private static ulong AudioPtsFromAccessUnitIndex(List<AudioAccessUnitIndexEntry> audioIndex, int consumedAus)
        {
            if (consumedAus <= 0)
                return PtsAudioStart;

            if (consumedAus >= audioIndex.Count)
                consumedAus = audioIndex.Count - 1;

            ulong tick240k = audioIndex[consumedAus].EndTick240K;
            return PtsAudioStart + ((tick240k * 3UL + 4UL) / 8UL);
        }

        private static ulong ComputeInitialScr(bool initialStartupMode)
        {
            return initialStartupMode ? ScrInitialStartup27M : ScrInitial27M;
        }

        private static ulong BfTargetForSegment(List<int> bfFrameStarts, int bfIndex, bool initialStartupMode)
        {
            int nextSyncFrame = bfFrameStarts[bfIndex];
            ulong syncDts = VideoPtsDts(nextSyncFrame).dts;
            ulong segLead = VideoSegLeadForSegment(bfIndex, bfFrameStarts.Count, initialStartupMode);
            return syncDts * 300 > segLead ? syncDts * 300 - segLead : 0;
        }

        private static ulong ComputePlainVideoTarget(long[] frameByteStart, long totalVideoBytes, long vCursor, int payloadLen, int bfIndex, long nextBfThreshold, List<int> bfFrameStarts)
        {
            if (payloadLen <= 0 || vCursor >= totalVideoBytes)
                return 0;

            if (bfIndex < bfFrameStarts.Count && vCursor + payloadLen >= nextBfThreshold)
            {
                int nextSyncFrame = bfFrameStarts[bfIndex];
                ulong syncDts = VideoPtsDts(nextSyncFrame).dts;
                return syncDts * 300 > ScrLeadVideoMid ? syncDts * 300 - ScrLeadVideoMid : 0;
            }

            long endPos = Math.Min(totalVideoBytes - 1, vCursor + payloadLen - 1);
            int endFrame = FrameAt(frameByteStart, endPos);
            ulong dts = VideoPtsDts(endFrame).dts;
            return dts * 300 > ScrLeadVideoMid ? dts * 300 - ScrLeadVideoMid : 0;
        }

        private static bool IsMidFrameContinuation(long[] frameByteStart, long totalVideoBytes, long vCursor, int payloadLen)
        {
            if (payloadLen <= 0 || vCursor >= totalVideoBytes)
                return false;

            int startFrame = FrameAt(frameByteStart, vCursor);
            if (frameByteStart[startFrame] == vCursor)
                return false;

            long endPos = Math.Min(totalVideoBytes - 1, vCursor + payloadLen - 1);
            int endFrame = FrameAt(frameByteStart, endPos);
            return startFrame == endFrame;
        }

        private static bool TryGetDeepContinuationTarget(long[] frameByteStart, long totalVideoBytes, long vCursor, int payloadLen, int segmentStartFrame, ulong scr27, bool priorityMode, out ulong target, out int maxBytesToTargetUsed, out long bytesToTargetUsed)
        {
            target = 0;
            maxBytesToTargetUsed = NoDeepContinuationGate;
            bytesToTargetUsed = long.MaxValue;
            if (payloadLen <= 0 || vCursor >= totalVideoBytes)
                return false;

            int frame = FrameAt(frameByteStart, vCursor);
            if (frameByteStart[frame] == vCursor)
                return false;

            if (frame <= segmentStartFrame)
                return false;

            long frameOffset = vCursor - frameByteStart[frame];
            bool offsetReady = true;
            if (frame <= segmentStartFrame + 1)
            {
                if (frameOffset < VideoPayload * 4)
                    offsetReady = false;
            }
            else if (frame <= segmentStartFrame + 3 && frameOffset >= VideoPayload && frameOffset < VideoPayload * 3)
            {
                offsetReady = false;
            }
            else if (!priorityMode && frameOffset > 1024 && frameOffset < VideoPayload)
            {
                offsetReady = false;
            }

            int firstTargetFrame = frame + 1;
            if (IsAfterDeepContinuationStart(segmentStartFrame))
            {
                long payloadEnd = Math.Min(totalVideoBytes - 1, vCursor + payloadLen - 1);
                int payloadEndFrame = FrameAt(frameByteStart, payloadEnd);
                if (payloadEndFrame > frame)
                    firstTargetFrame = payloadEndFrame;
            }

            ulong deepTolerance = DeepContinuationTolerance27M;
            for (int targetFrame = firstTargetFrame; targetFrame < frameByteStart.Length; targetFrame++)
            {
                ulong dts = VideoPtsDts(targetFrame).dts;
                target = dts * 300 > ScrLeadVideoMid ? dts * 300 - ScrLeadVideoMid : 0;
                if (target > scr27 + deepTolerance)
                {
                    int targetFromSegment = targetFrame - segmentStartFrame;
                    long bytesToTarget = frameByteStart[targetFrame] - vCursor;
                    if (IsAfterDeepContinuationStart(segmentStartFrame))
                    {
                        if (bytesToTarget < DeepContinuationMinBytesToTarget)
                            continue;

                        if (bytesToTarget >= VideoPayload)
                        {
                            target = 0;
                            return false;
                        }
                    }

                    if (TryGetDeepContinuationMaxBytesToTarget(targetFromSegment, out int maxBytesToTarget))
                    {
                        maxBytesToTargetUsed = maxBytesToTarget;
                        bytesToTargetUsed = bytesToTarget;

                        if (maxBytesToTarget == NoDeepContinuationGate)
                        {
                            target = 0;
                            return false;
                        }
                        else if (bytesToTarget > maxBytesToTarget)
                        {
                            target = 0;
                            return false;
                        }
                    }
                    else if (!offsetReady)
                    {
                        target = 0;
                        return false;
                    }

                    return true;
                }
            }

            target = 0;
            return false;
        }

        private static bool TryGetDeepContinuationMaxBytesToTarget(int targetFrameOffset, out int maxBytesToTarget)
        {
            maxBytesToTarget = 0;
            if (targetFrameOffset <= 0 ||
                targetFrameOffset >= DeepContinuationMaxBytesToTargetByFrameOffset.Length)
            {
                return false;
            }

            maxBytesToTarget = DeepContinuationMaxBytesToTargetByFrameOffset[targetFrameOffset];
            return true;
        }

        private static int GetPlainPayloadLength(long vCursor, long totalVideoBytes, int bfIndex, long nextBfThreshold, List<int> bfFrameStarts)
        {
            if (vCursor >= totalVideoBytes)
                return 0;

            int cap = VideoPayload;
            if (bfIndex < bfFrameStarts.Count)
                cap = (int)Math.Min(cap, nextBfThreshold - vCursor);

            return (int)Math.Min(cap, totalVideoBytes - vCursor);
        }

        private static bool TryGetNextPlainVideoTarget(long vCursor, long totalVideoBytes, long[] frameByteStart, int bfIndex, long nextBfThreshold, List<int> bfFrameStarts, ulong scr27, out ulong plainTarget)
        {
            plainTarget = 0;
            int len = GetPlainPayloadLength(vCursor, totalVideoBytes, bfIndex, nextBfThreshold, bfFrameStarts);
            if (len <= 0)
                return false;

            bool isTailBeforeBf = bfIndex < bfFrameStarts.Count && vCursor + len >= nextBfThreshold && len < VideoPayload;
            if (isTailBeforeBf)
            {
                plainTarget = ComputePlainVideoTarget(frameByteStart, totalVideoBytes, vCursor, len, bfIndex, nextBfThreshold, bfFrameStarts);
                return true;
            }

            if (IsMidFrameContinuation(frameByteStart, totalVideoBytes, vCursor, len))
            {
                plainTarget = scr27;
                return true;
            }

            plainTarget = ComputePlainVideoTarget(frameByteStart, totalVideoBytes, vCursor, len, bfIndex, nextBfThreshold, bfFrameStarts);
            return true;
        }

        private static ulong RequiredPacksBeforeTimestamp(long vCursor, long totalVideoBytes, long[] frameByteStart, int nextPtsFrame)
        {
            if (frameByteStart == null || nextPtsFrame < 0 || nextPtsFrame >= frameByteStart.Length || vCursor >= totalVideoBytes)
                return 0;

            long tsBound = frameByteStart[nextPtsFrame];
            int tsPayload = VideoPayload - 10;
            long tsWindowStart = tsBound > tsPayload ? tsBound - tsPayload : 0;
            long bytesBeforeTs = tsWindowStart > vCursor ? tsWindowStart - vCursor : 0;
            ulong plainPacks = (ulong)((bytesBeforeTs + VideoPayload - 1) / VideoPayload);
            return plainPacks + 1;
        }

        private static ushort AtxExt16(long cursor, int blockAlign)
        {
            long rem = cursor % blockAlign;
            long firstAuBytes = (blockAlign - rem) % blockAlign;
            long globalFrame = (cursor + firstAuBytes) / blockAlign;
            int atxExtPeriod = blockAlign + 8;
            long val = (firstAuBytes + globalFrame * 8) % atxExtPeriod;
            return (ushort)(val & 0xFFFF);
        }

        private static ulong PackDuration27M()
        {
            return (PackDurationNum + PackDurationDen - 1) / PackDurationDen;
        }

        private static int FrameAt(long[] starts, long pos)
        {
            int lo = 0, hi = starts.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (starts[mid] <= pos) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        private static List<int[]> BuildBfSegments(IReadOnlyList<VideoAccessUnitIndexEntry> videoIndex, int[] frameSizes, List<int> frameStarts)
        {
            frameStarts.Clear();
            var syncFrames = new List<int> { 0 };

            for (int i = 1; i < videoIndex.Count; i++)
            {
                if (videoIndex[i].IsSync)
                    syncFrames.Add(i);
            }

            var segments = new List<int[]>();
            for (int i = 0; i < syncFrames.Count; i++)
            {
                int start = syncFrames[i];
                int end = i + 1 < syncFrames.Count ? syncFrames[i + 1] : videoIndex.Count;
                int count = end - start;
                if (count <= 0)
                    continue;

                int[] seg = new int[count];
                for (int j = 0; j < count; j++)
                    seg[j] = frameSizes[start + j];
                frameStarts.Add(start);
                segments.Add(seg);
            }

            return segments;
        }

        private static int TimestampStepForSegment(int segmentIndex, int segmentCount, bool initialStartupMode)
        {
            if (initialStartupMode && segmentIndex < 2)
                return PtsStampIntervalFinal;

            if (segmentIndex == segmentCount - 2 && segmentIndex >= 0)
                return PtsStampInterval;

            return segmentIndex == segmentCount - 1 ? PtsStampIntervalFinal : PtsStampInterval;
        }

        private static int SegmentEndFrame(List<int> frameStarts, int segmentIndex, int totalFrames)
        {
            return segmentIndex + 1 < frameStarts.Count ? frameStarts[segmentIndex + 1] : totalFrames;
        }

        private static int SegmentStartFrame(List<int> frameStarts, int segmentIndex)
        {
            if (frameStarts == null || frameStarts.Count == 0 || segmentIndex <= 0)
                return 0;

            if (segmentIndex >= frameStarts.Count)
                return frameStarts[frameStarts.Count - 1];

            return frameStarts[segmentIndex];
        }

        private static bool IsDeepContinuationRegion(List<int> frameStarts, int segmentIndex)
        {
            return SegmentStartFrame(frameStarts, segmentIndex) >= DeepContinuationStartFrame;
        }

        private static bool IsAfterDeepContinuationStart(int segmentStartFrame)
        {
            return segmentStartFrame > DeepContinuationStartFrame;
        }

        private static bool IsLateTimestampGateRegion(int segmentStartFrame)
        {
            return segmentStartFrame >= LateTimestampGateStartFrame;
        }

        private static bool IsNearStreamEnd(int totalFrames, int segmentStartFrame)
        {
            return totalFrames - segmentStartFrame <= TailTimestampGateMaxFramesRemaining;
        }

        private static ulong VideoSegLeadForSegment(int segmentIndex, int segmentCount, bool initialStartupMode)
        {
            if (initialStartupMode && segmentIndex < 2)
                return ScrLeadVideoStartup;

            return segmentIndex == segmentCount - 1 ? ScrLeadVideoStartup : ScrLeadVideoSeg;
        }

        private static ulong VideoTsLeadForSegment(int segmentIndex, int segmentCount, int segmentStartFrame, int stampedFrame, bool isTailTs, bool initialStartupMode)
        {
            if (initialStartupMode && segmentIndex == 0)
                return isTailTs ? ScrLeadVideoInitialStartupTail : ScrLeadVideoStartup;

            if (initialStartupMode && segmentIndex == 1 && stampedFrame <= segmentStartFrame + PtsStampIntervalFinal)
                return ScrLeadVideoStartup;

            if (segmentIndex == segmentCount - 1)
                return isTailTs ? ScrLeadVideoStartupTail : ScrLeadVideoStartup;

            if (isTailTs && segmentIndex == segmentCount - 2)
                return ScrLeadVideoCross;

            return ScrLeadVideoMid;
        }

        private static byte[] BuildPackHeader(ulong scrBase, ulong scrExt)
        {
            byte[] h = new byte[14];
            h[0] = 0x00; h[1] = 0x00; h[2] = 0x01; h[3] = 0xBA;
            h[4] = (byte)(0x44 | (((scrBase >> 30) & 0x07) << 3) | ((scrBase >> 28) & 0x03));
            h[5] = (byte)((scrBase >> 20) & 0xFF);
            h[6] = (byte)((((scrBase >> 15) & 0x1F) << 3) | 0x04 | ((scrBase >> 13) & 0x03));
            h[7] = (byte)((scrBase >> 5) & 0xFF);
            h[8] = (byte)(((scrBase & 0x1F) << 3) | 0x04 | ((scrExt >> 7) & 0x03));
            h[9] = (byte)(((scrExt & 0x7F) << 1) | 0x01);
            h[10] = (byte)((MuxRate >> 14) & 0xFF);
            h[11] = (byte)((MuxRate >> 6) & 0xFF);
            h[12] = (byte)(((MuxRate & 0x3F) << 2) | 0x03);
            h[13] = 0xF8;
            return h;
        }

        private static byte[] BuildSystemHeader()
        {
            byte[] payload =
            {
                0x80, 0xC3, 0x51, 0x80, 0xF0, 0x7F, 0xB9, 0xE0, 0xFB, 0xBD, 0xE0, 0x08
            };

            byte[] r = new byte[6 + payload.Length];
            r[0] = 0x00; r[1] = 0x00; r[2] = 0x01; r[3] = SidSystem;
            r[4] = (byte)((payload.Length >> 8) & 0xFF);
            r[5] = (byte)(payload.Length & 0xFF);
            Buffer.BlockCopy(payload, 0, r, 6, payload.Length);
            return r;
        }

        private static byte[] BuildBfPes(int[] seg)
        {
            byte[] preTable = new byte[12];

            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x01); ms.WriteByte(SidVideo);
                ms.Write(preTable, 0, preTable.Length);
                int tableSize = 2 + seg.Length * 4;
                ms.WriteByte((byte)((tableSize >> 8) & 0xFF));
                ms.WriteByte((byte)(tableSize & 0xFF));
                ms.WriteByte((byte)((seg.Length >> 8) & 0xFF));
                ms.WriteByte((byte)(seg.Length & 0xFF));

                for (int i = 0; i < seg.Length; i++)
                {
                    bool isLast = i == seg.Length - 1;
                    ms.WriteByte(0x04); ms.WriteByte(isLast ? (byte)0x00 : (byte)0x80);
                    int s = seg[i];
                    ms.WriteByte((byte)((s >> 8) & 0xFF));
                    ms.WriteByte((byte)(s & 0xFF));
                }

                byte[] payload = ms.ToArray();
                byte[] r = new byte[6 + payload.Length];
                r[0] = 0x00; r[1] = 0x00; r[2] = 0x01; r[3] = SidPriv2;
                r[4] = (byte)((payload.Length >> 8) & 0xFF);
                r[5] = (byte)(payload.Length & 0xFF);
                Buffer.BlockCopy(payload, 0, r, 6, payload.Length);
                return r;
            }
        }

        private static byte[] BuildPadding(int size)
        {
            if (size < 6) return new byte[size];
            byte[] r = new byte[size];
            r[0] = 0x00; r[1] = 0x00; r[2] = 0x01; r[3] = SidPadding;
            int pay = size - 6;
            r[4] = (byte)((pay >> 8) & 0xFF); r[5] = (byte)(pay & 0xFF);
            for (int i = 6; i < r.Length; i++) r[i] = 0xFF;
            return r;
        }

        private static byte[] BuildVideoNoTsPes(byte[] payload)
        {
            int pesLen = 3 + payload.Length;
            byte[] r = new byte[6 + 3 + payload.Length];
            r[0] = 0x00; r[1] = 0x00; r[2] = 0x01; r[3] = SidVideo;
            r[4] = (byte)((pesLen >> 8) & 0xFF); r[5] = (byte)(pesLen & 0xFF);
            r[6] = 0x81; r[7] = 0x00; r[8] = 0x00;
            Buffer.BlockCopy(payload, 0, r, 9, payload.Length);
            return r;
        }

        private static byte[] BuildVideoTsPes(byte[] payload, ulong pts, ulong dts)
        {
            byte[] opt = EncodePtsDts(pts, dts);
            int pesLen = 3 + 10 + payload.Length;
            byte[] r = new byte[6 + 3 + 10 + payload.Length];
            r[0] = 0x00; r[1] = 0x00; r[2] = 0x01; r[3] = SidVideo;
            r[4] = (byte)((pesLen >> 8) & 0xFF); r[5] = (byte)(pesLen & 0xFF);
            r[6] = 0x81; r[7] = 0xC0; r[8] = 0x0A;
            Buffer.BlockCopy(opt, 0, r, 9, opt.Length);
            Buffer.BlockCopy(payload, 0, r, 19, payload.Length);
            return r;
        }

        private static byte[] BuildVideoSegPes(byte[] payload, ulong pts, ulong dts)
        {
            byte[] opt = EncodePtsDts(pts, dts);
            byte[] subHdr = { 0x1E, 0x60, 0xFB };
            int pesLen = 3 + 10 + 3 + payload.Length;
            byte[] r = new byte[6 + 3 + 10 + 3 + payload.Length];
            r[0] = 0x00; r[1] = 0x00; r[2] = 0x01; r[3] = SidVideo;
            r[4] = (byte)((pesLen >> 8) & 0xFF); r[5] = (byte)(pesLen & 0xFF);
            r[6] = 0x81; r[7] = 0xC1; r[8] = 0x0D;
            Buffer.BlockCopy(opt, 0, r, 9, opt.Length);
            Buffer.BlockCopy(subHdr, 0, r, 19, subHdr.Length);
            Buffer.BlockCopy(payload, 0, r, 22, payload.Length);
            return r;
        }

        private static byte[] BuildAudioPesFirst(byte[] payload, ulong pts, ushort ext16, byte subId)
        {
            byte[] ptsBuf = EncodeTs(pts, 0x21);
            byte[] opt = Concat(ptsBuf, new byte[] { 0x1E, 0x60, 0x04 });
            byte[] priv = { subId, 0x00, (byte)((ext16 >> 8) & 0xFF), (byte)(ext16 & 0xFF) };
            int pesLen = 3 + 8 + priv.Length + payload.Length;
            byte[] r = new byte[6 + 3 + 8 + priv.Length + payload.Length];
            r[0] = 0x00; r[1] = 0x00; r[2] = 0x01; r[3] = SidAudio;
            r[4] = (byte)((pesLen >> 8) & 0xFF); r[5] = (byte)(pesLen & 0xFF);
            r[6] = 0x81; r[7] = 0x81; r[8] = 0x08;
            int off = 9;
            Buffer.BlockCopy(opt, 0, r, off, opt.Length); off += opt.Length;
            Buffer.BlockCopy(priv, 0, r, off, priv.Length); off += priv.Length;
            Buffer.BlockCopy(payload, 0, r, off, payload.Length);
            return r;
        }

        private static byte[] BuildAudioPes(byte[] payload, ulong pts, ushort ext16, byte subId)
        {
            byte[] ptsBuf = EncodeTs(pts, 0x21);
            byte[] priv = { subId, 0x00, (byte)((ext16 >> 8) & 0xFF), (byte)(ext16 & 0xFF) };
            int pesLen = 3 + 5 + priv.Length + payload.Length;
            byte[] r = new byte[6 + 3 + 5 + priv.Length + payload.Length];
            r[0] = 0x00; r[1] = 0x00; r[2] = 0x01; r[3] = SidAudio;
            r[4] = (byte)((pesLen >> 8) & 0xFF); r[5] = (byte)(pesLen & 0xFF);
            r[6] = 0x81; r[7] = 0x80; r[8] = 0x05;
            int off = 9;
            Buffer.BlockCopy(ptsBuf, 0, r, off, ptsBuf.Length); off += ptsBuf.Length;
            Buffer.BlockCopy(priv, 0, r, off, priv.Length); off += priv.Length;
            Buffer.BlockCopy(payload, 0, r, off, payload.Length);
            return r;
        }

        private static byte[] EncodePtsDts(ulong pts, ulong dts) => Concat(EncodeTs(pts, 0x31), EncodeTs(dts, 0x11));

        private static byte[] EncodeTs(ulong ts, byte marker)
        {
            ts &= (1UL << 33) - 1;
            return new byte[]
            {
                (byte)(marker | (((ts >> 30) & 0x07) << 1) | 0x01),
                (byte)((ts >> 22) & 0xFF),
                (byte)((((ts >> 15) & 0x7F) << 1) | 0x01),
                (byte)((ts >> 7) & 0xFF),
                (byte)(((ts & 0x7F) << 1) | 0x01),
            };
        }

        private static byte[] Concat(byte[] a, byte[] b)
        {
            byte[] r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }
    }
}
