using System;
using System.IO;

namespace oMPSComposer
{
    internal sealed class AtracReader
    {
        private static readonly byte[] StandardAtrac3PlusGuid =
        {
            0xBF, 0xAA, 0x23, 0xE9, 0x58, 0xCB, 0x71, 0x44,0xA1, 0x19, 0xFF, 0xFA, 0x01, 0xE4, 0xCE, 0x62
        };

        private static readonly byte[] PspAtxGuid =
        {
            0x0D, 0x5C, 0x13, 0x50, 0x1B, 0xA2, 0xEC, 0x4B,0x91, 0xED, 0x6E, 0xDD, 0xB8, 0x3C, 0x8C, 0x66
        };

        public byte[] Data { get; }
        public int BlockAlign { get; }
        public int PacketSize { get; }
        public int FrameCount { get; }
        public int SampleRate { get; }
        public byte[] AuHeader { get; }

        private AtracReader(byte[] data, int blockAlign, int sampleRate, byte[] auHeader)
        {
            Data = data;
            BlockAlign = blockAlign;
            PacketSize = blockAlign + (auHeader?.Length ?? 0);
            SampleRate = sampleRate;
            AuHeader = auHeader;
            FrameCount = CountAccessUnits(data, PacketSize, auHeader);
        }

        public static AtracReader Open(string path)
        {
            byte[] file = File.ReadAllBytes(path);
            if (!IsRiff(file))
                throw new InvalidDataException("'" + path + "' is not a valid audio file.");

            AtracReader reader = FromRiff(file);
            return new AtracReader(reader.Data, reader.BlockAlign, reader.SampleRate, reader.AuHeader);
        }

        private static AtracReader FromRiff(byte[] file)
        {
            int blockAlign = 744;
            int sampleRate = 44100;
            byte[] fmtChunk = null;
            byte[] dataBody = null;
            bool hasFact = false;

            int pos = 12;
            while (pos + 8 <= file.Length)
            {
                int chunkSize = ReadI32Le(file, pos + 4);
                int dataStart = pos + 8;

                if (Tag(file, pos, 'f', 'm', 't', ' ') && chunkSize >= 14)
                {
                    fmtChunk = Sub(file, dataStart, chunkSize);
                    sampleRate = ReadI32Le(file, dataStart + 4);
                    blockAlign = ReadU16Le(file, dataStart + 12);
                }
                else if (Tag(file, pos, 'f', 'a', 'c', 't'))
                {
                    hasFact = true;
                }
                else if (Tag(file, pos, 'd', 'a', 't', 'a'))
                {
                    dataBody = Sub(file, dataStart, chunkSize);
                }

                pos = dataStart + ((chunkSize + 1) & ~1);
            }

            if (dataBody == null)
                throw new InvalidDataException("No 'data' chunk found in AT3 file.");

            byte[] auHeader = BuildAuHeader(sampleRate, blockAlign);
            byte[] streamData = NormalizeStreamData(dataBody, fmtChunk, hasFact, blockAlign, auHeader);
            return new AtracReader(streamData, blockAlign, sampleRate, auHeader);
        }

        private static byte[] NormalizeStreamData(byte[] dataBody, byte[] fmtChunk, bool hasFact, int blockAlign, byte[] auHeader)
        {
            if (LooksLikeAtxData(dataBody, blockAlign, auHeader))
                return dataBody;

            bool pspAtxContainer = HasSubtypeGuid(fmtChunk, PspAtxGuid);
            bool standardAt3Container = hasFact || HasSubtypeGuid(fmtChunk, StandardAtrac3PlusGuid);

            if (pspAtxContainer)
                return dataBody;

            if (!standardAt3Container && dataBody.Length < blockAlign)
                return dataBody;

            return InsertAuHeaders(dataBody, blockAlign, auHeader);
        }

        private static bool LooksLikeAtxData(byte[] dataBody, int blockAlign, byte[] auHeader)
        {
            int packetSize = blockAlign + auHeader.Length;
            if (dataBody == null || dataBody.Length < auHeader.Length || packetSize <= auHeader.Length)
                return false;

            if (!MatchesAt(dataBody, 0, auHeader))
                return false;

            int checks = Math.Min(3, dataBody.Length / packetSize);
            for (int i = 1; i < checks; i++)
            {
                if (!MatchesAt(dataBody, i * packetSize, auHeader))
                    return false;
            }

            return true;
        }

        private static byte[] InsertAuHeaders(byte[] payloadData, int blockAlign, byte[] auHeader)
        {
            byte[] alignedPayload = TrimAlign(payloadData, blockAlign);
            if (alignedPayload.Length == 0)
                return alignedPayload;

            int frameCount = alignedPayload.Length / blockAlign;
            int packetSize = blockAlign + auHeader.Length;
            byte[] result = new byte[frameCount * packetSize];

            for (int i = 0; i < frameCount; i++)
            {
                int srcOffset = i * blockAlign;
                int dstOffset = i * packetSize;
                Buffer.BlockCopy(auHeader, 0, result, dstOffset, auHeader.Length);
                Buffer.BlockCopy(alignedPayload, srcOffset, result, dstOffset + auHeader.Length, blockAlign);
            }

            return result;
        }

        private static byte[] TrimAlign(byte[] data, int blockAlign)
        {
            int rem = data.Length % blockAlign;
            if (rem == 0)
                return data;

            byte[] trimmed = new byte[data.Length - rem];
            Buffer.BlockCopy(data, 0, trimmed, 0, trimmed.Length);
            return trimmed;
        }

        private static byte[] Sub(byte[] src, int offset, int length)
        {
            length = Math.Min(length, src.Length - offset);
            byte[] r = new byte[length];
            Buffer.BlockCopy(src, offset, r, 0, length);
            return r;
        }

        private static int CountAccessUnits(byte[] data, int packetSize, byte[] auHeader)
        {
            if (data == null || data.Length == 0 || packetSize <= 0)
                return 0;

            if (auHeader == null || auHeader.Length == 0 || !MatchesAt(data, 0, auHeader))
                return data.Length / packetSize;

            int count = 0;
            for (int offset = 0; offset + packetSize <= data.Length; offset += packetSize)
            {
                if (!MatchesAt(data, offset, auHeader))
                    break;

                count++;
            }

            return count;
        }

        private static byte[] BuildAuHeader(int sampleRate, int blockAlign)
        {
            byte fmtHi = sampleRate == 48000 ? (byte)0x44 : (byte)0x28;
            byte fmtLo = (byte)((blockAlign - 8) >> 3);
            return new byte[] { 0x0F, 0xD0, fmtHi, fmtLo, 0x00, 0x00, 0x00, 0x00 };
        }

        private static bool HasSubtypeGuid(byte[] fmtChunk, byte[] guid)
        {
            if (fmtChunk == null || fmtChunk.Length < 40)
                return false;

            return MatchesAt(fmtChunk, 24, guid);
        }

        private static bool MatchesAt(byte[] data, int offset, byte[] value)
        {
            if (data == null || value == null || offset < 0 || offset + value.Length > data.Length)
                return false;

            for (int i = 0; i < value.Length; i++)
                if (data[offset + i] != value[i])
                    return false;

            return true;
        }

        private static bool IsRiff(byte[] d)
        {
            return d.Length >= 12 && d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W' && d[9] == 'A' && d[10] == 'V' && d[11] == 'E';
        }

        private static bool Tag(byte[] d, int p, char a, char b, char c, char e)
        {
            return d[p] == (byte)a && d[p + 1] == (byte)b && d[p + 2] == (byte)c && d[p + 3] == (byte)e;
        }

        private static int ReadI32Le(byte[] d, int p)
        {
            return d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24);
        }

        private static ushort ReadU16Le(byte[] d, int p)
        {
            return (ushort)(d[p] | (d[p + 1] << 8));
        }
    }
}
