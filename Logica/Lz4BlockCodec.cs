using System;
using System.IO;

namespace RobloxScriptExplorer.Logica
{
    public static class Lz4BlockCodec
    {
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

        public static byte[] Compress(byte[] data)
        {
            int n = data.Length;
            if (n == 0) return new byte[] { 0 };

            using var ms = new MemoryStream(n + 64);
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
