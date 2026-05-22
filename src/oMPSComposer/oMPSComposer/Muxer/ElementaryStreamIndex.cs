using System;
using System.Collections.Generic;

namespace oMPSComposer
{
    internal sealed class VideoAccessUnitIndexEntry
    {
        public int Size { get; }
        public ulong StartTick240K { get; }
        public ulong EndTick240K { get; }
        public ushort Type { get; }
        public ushort Flags { get; }
        public bool IsSync => Type == 0x0001;

        public VideoAccessUnitIndexEntry(int size, ulong startTick240K, ulong endTick240K, ushort type, ushort flags)
        {
            Size = size;
            StartTick240K = startTick240K;
            EndTick240K = endTick240K;
            Type = type;
            Flags = flags;
        }
    }

    internal sealed class AudioAccessUnitIndexEntry
    {
        public int Size { get; }
        public ulong EndTick240K { get; }
        public ushort Type { get; }

        public AudioAccessUnitIndexEntry(int size, ulong endTick240K, ushort type)
        {
            Size = size;
            EndTick240K = endTick240K;
            Type = type;
        }
    }

    internal static class ElementaryStreamIndex
    {
        private const int VideoTickStep240K = 8_008;
        private const int AudioTickRate240K = 240_000;
        private const int AtracSamplesPerAccessUnit = 2_048;

        public static List<VideoAccessUnitIndexEntry> BuildVideoFromAnnexB(IReadOnlyList<byte[]> accessUnits)
        {
            var index = new List<VideoAccessUnitIndexEntry>(accessUnits.Count);
            ulong start = 0;

            for (int i = 0; i < accessUnits.Count; i++)
            {
                byte[] accessUnit = accessUnits[i];
                ulong end = start + VideoTickStep240K;
                bool isSync = IsSyncAccessUnit(accessUnit);
                ushort type = (ushort)(isSync ? 0x0001 : 0x0003);
                ushort flags = (ushort)(isSync ? 0x0109 : (i == accessUnits.Count - 1 ? 0x0008 : 0x0009));
                index.Add(new VideoAccessUnitIndexEntry(accessUnit.Length, start, end, type, flags));
                start = end;
            }

            return index;
        }

        public static List<AudioAccessUnitIndexEntry> BuildAudioFromAtx(int accessUnitCount, int packetSize, int sampleRate)
        {
            var index = new List<AudioAccessUnitIndexEntry>(accessUnitCount);
            ulong tickBase;
            ulong tickFrac;
            ulong carry;
            ulong tick = 0;

            if (sampleRate <= 0)
                sampleRate = 44_100;

            if (sampleRate == 44_100)
            {
                tickBase = 11_145;
                tickFrac = 25_500;
                carry = 21_300;
            }
            else
            {
                ulong tickNum = (ulong)AudioTickRate240K * AtracSamplesPerAccessUnit;
                ulong sampleRateU = (ulong)sampleRate;
                tickBase = tickNum / sampleRateU;
                tickFrac = tickNum % sampleRateU;
                carry = sampleRateU / 2UL;
            }

            for (int i = 0; i < accessUnitCount; i++)
            {
                if (i > 0)
                {
                    ulong sampleRateU = (ulong)sampleRate;
                    carry += tickFrac;
                    tick += tickBase + carry / sampleRateU;
                    carry %= sampleRateU;
                }

                index.Add(new AudioAccessUnitIndexEntry(packetSize, tick, 0x0001));
            }

            return index;
        }

        private static bool IsSyncAccessUnit(byte[] accessUnit)
        {
            bool hasIdr = false;
            bool hasSps = false;
            bool hasPps = false;

            for (int i = 0; i < accessUnit.Length - 4; i++)
            {
                int prefix = 0;
                if (accessUnit[i] == 0 && accessUnit[i + 1] == 0)
                {
                    if (accessUnit[i + 2] == 1)
                        prefix = 3;
                    else if (i + 3 < accessUnit.Length && accessUnit[i + 2] == 0 && accessUnit[i + 3] == 1)
                        prefix = 4;
                }

                if (prefix == 0 || i + prefix >= accessUnit.Length)
                    continue;

                int nalType = accessUnit[i + prefix] & 0x1F;
                hasIdr |= nalType == 5;
                hasSps |= nalType == 7;
                hasPps |= nalType == 8;

                if (hasIdr && hasSps && hasPps)
                    return true;

                i += prefix - 1;
            }

            return false;
        }
    }
}
