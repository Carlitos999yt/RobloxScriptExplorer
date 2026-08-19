using System;
using System.IO;

namespace RobloxScriptExplorer.Logica
{
    /// <summary>
    /// Codec LZ4 de ultra alto rendimiento en C# puro para comprimir y descomprimir chunks de Roblox (.rbxl / .rbxm).
    /// </summary>
    public static class Lz4BlockCodec
    {
        private const int MaxDistance = 65535;
        private const int HashLog = 14; // 16384 entries (64KB tabla hash)
        private const int HashSize = 1 << HashLog;
        private const uint Prime4Bytes = 2654435761u;

        public static byte[] Decompress(byte[] src, int uncompressedSize)
        {
            byte[] outBuf = new byte[uncompressedSize];
            int outPos = 0;
            int srcPos = 0;
            int srcLen = src.Length;

            while (srcPos < srcLen && outPos < uncompressedSize)
            {
                byte token = src[srcPos++];
                int litLen = token >> 4;
                if (litLen == 15)
                {
                    while (srcPos < srcLen)
                    {
                        byte b = src[srcPos++];
                        litLen += b;
                        if (b != 255) break;
                    }
                }

                if (litLen > 0)
                {
                    if (srcPos + litLen > srcLen) litLen = srcLen - srcPos;
                    if (outPos + litLen > uncompressedSize) litLen = uncompressedSize - outPos;

                    Buffer.BlockCopy(src, srcPos, outBuf, outPos, litLen);
                    srcPos += litLen;
                    outPos += litLen;
                }

                if (outPos >= uncompressedSize || srcPos >= srcLen) break;

                if (srcPos + 2 > srcLen) break;
                int offset = src[srcPos] | (src[srcPos + 1] << 8);
                srcPos += 2;
                if (offset == 0) break;

                int matchLen = token & 0x0F;
                if (matchLen == 15)
                {
                    while (srcPos < srcLen)
                    {
                        byte b = src[srcPos++];
                        matchLen += b;
                        if (b != 255) break;
                    }
                }
                matchLen += 4;

                if (outPos + matchLen > uncompressedSize) matchLen = uncompressedSize - outPos;

                int matchSrc = outPos - offset;
                if (offset >= matchLen)
                {
                    Buffer.BlockCopy(outBuf, matchSrc, outBuf, outPos, matchLen);
                    outPos += matchLen;
                }
                else
                {
                    while (matchLen > 0)
                    {
                        int copyLen = Math.Min(matchLen, offset);
                        Buffer.BlockCopy(outBuf, matchSrc, outBuf, outPos, copyLen);
                        outPos += copyLen;
                        matchLen -= copyLen;
                    }
                }
            }

            return outBuf;
        }

        public static byte[] Compress(byte[] src)
        {
            int srcLen = src.Length;
            if (srcLen == 0) return new byte[] { 0 };
            if (srcLen < 13)
            {
                return EmitLiteralBlock(src);
            }

            int maxOut = srcLen + (srcLen / 255) + 32;
            byte[] dst = new byte[maxOut];
            int dstPos = 0;

            int[] hashTable = new int[HashSize];
            Array.Fill(hashTable, -1);

            int anchor = 0;
            int srcPos = 0;
            int limit = srcLen - 5;

            while (srcPos < limit)
            {
                uint val = (uint)(src[srcPos] | (src[srcPos + 1] << 8) | (src[srcPos + 2] << 16) | (src[srcPos + 3] << 24));
                uint hash = (val * Prime4Bytes) >> (32 - HashLog);
                int refPos = hashTable[hash];
                hashTable[hash] = srcPos;

                if (refPos != -1 && (srcPos - refPos) <= MaxDistance && (srcPos - refPos) > 0 &&
                    src[refPos] == src[srcPos] &&
                    src[refPos + 1] == src[srcPos + 1] &&
                    src[refPos + 2] == src[srcPos + 2] &&
                    src[refPos + 3] == src[srcPos + 3])
                {
                    // Match found!
                    int matchLen = 4;
                    while (srcPos + matchLen < srcLen && src[refPos + matchLen] == src[srcPos + matchLen])
                    {
                        matchLen++;
                    }

                    int litLen = srcPos - anchor;
                    int tokenPos = dstPos++;
                    int tokenLit = Math.Min(litLen, 15);

                    if (litLen >= 15)
                    {
                        int rem = litLen - 15;
                        while (rem >= 255)
                        {
                            dst[dstPos++] = 255;
                            rem -= 255;
                        }
                        dst[dstPos++] = (byte)rem;
                    }

                    if (litLen > 0)
                    {
                        Buffer.BlockCopy(src, anchor, dst, dstPos, litLen);
                        dstPos += litLen;
                    }

                    int offset = srcPos - refPos;
                    dst[dstPos++] = (byte)(offset & 0xFF);
                    dst[dstPos++] = (byte)((offset >> 8) & 0xFF);

                    int matchLenAdjusted = matchLen - 4;
                    int tokenMatch = Math.Min(matchLenAdjusted, 15);
                    dst[tokenPos] = (byte)((tokenLit << 4) | tokenMatch);

                    if (matchLenAdjusted >= 15)
                    {
                        int rem = matchLenAdjusted - 15;
                        while (rem >= 255)
                        {
                            dst[dstPos++] = 255;
                            rem -= 255;
                        }
                        dst[dstPos++] = (byte)rem;
                    }

                    srcPos += matchLen;
                    anchor = srcPos;
                }
                else
                {
                    srcPos++;
                }
            }

            int remainingLit = srcLen - anchor;
            if (remainingLit > 0)
            {
                int tokenPos = dstPos++;
                int tokenLit = Math.Min(remainingLit, 15);
                dst[tokenPos] = (byte)(tokenLit << 4);

                if (remainingLit >= 15)
                {
                    int rem = remainingLit - 15;
                    while (rem >= 255)
                    {
                        dst[dstPos++] = 255;
                        rem -= 255;
                    }
                    dst[dstPos++] = (byte)rem;
                }

                Buffer.BlockCopy(src, anchor, dst, dstPos, remainingLit);
                dstPos += remainingLit;
            }

            byte[] result = new byte[dstPos];
            Buffer.BlockCopy(dst, 0, result, 0, dstPos);
            return result;
        }

        private static byte[] EmitLiteralBlock(byte[] data)
        {
            int n = data.Length;
            using var ms = new MemoryStream(n + 16);
            if (n < 15)
            {
                ms.WriteByte((byte)(n << 4));
            }
            else
            {
                ms.WriteByte(0xF0);
                int rem = n - 15;
                while (rem >= 255)
                {
                    ms.WriteByte(255);
                    rem -= 255;
                }
                ms.WriteByte((byte)rem);
            }
            ms.Write(data, 0, data.Length);
            return ms.ToArray();
        }
    }
}
