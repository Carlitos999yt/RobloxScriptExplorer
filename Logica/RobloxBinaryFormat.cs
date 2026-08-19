using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RobloxScriptExplorer.Logica
{
    public class RobloxChunk
    {
        public string Name { get; set; } = string.Empty;
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public byte[] RawCompressedData { get; set; } = Array.Empty<byte>();
        public uint CompLen { get; set; }
        public uint UncompLen { get; set; }
        public uint Reserved { get; set; }
        public bool IsModified { get; set; } = false;
    }

    public static class RobloxBinaryFormat
    {
        public static readonly byte[] RobloxMagic = new byte[] {
            0x3C, 0x72, 0x6F, 0x62, 0x6C, 0x6F, 0x78, 0x21, // <roblox!
            0x89, 0xFF, 0x0D, 0x0A, 0x1A, 0x0A              // \x89\xff\r\n\x1a\n
        };

        public static int DecodeZigZag(int n)
        {
            return (n >> 1) ^ (-(n & 1));
        }

        public static int EncodeZigZag(int n)
        {
            return (n << 1) ^ (n >> 31);
        }

        public static int[] DecodeIntArray(byte[] data, int offset, int count)
        {
            int[] res = new int[count];
            int c1 = count;
            int c2 = 2 * count;
            int c3 = 3 * count;

            for (int i = 0; i < count; i++)
            {
                int b0 = data[offset + i];
                int b1 = data[offset + c1 + i];
                int b2 = data[offset + c2 + i];
                int b3 = data[offset + c3 + i];
                int val = (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
                res[i] = DecodeZigZag(val);
            }
            return res;
        }

        public static byte[] EncodeIntArray(int[] values)
        {
            int count = values.Length;
            byte[] b0 = new byte[count];
            byte[] b1 = new byte[count];
            byte[] b2 = new byte[count];
            byte[] b3 = new byte[count];

            for (int i = 0; i < count; i++)
            {
                uint zz = (uint)EncodeZigZag(values[i]);
                b0[i] = (byte)((zz >> 24) & 0xFF);
                b1[i] = (byte)((zz >> 16) & 0xFF);
                b2[i] = (byte)((zz >> 8) & 0xFF);
                b3[i] = (byte)(zz & 0xFF);
            }

            byte[] result = new byte[count * 4];
            Buffer.BlockCopy(b0, 0, result, 0, count);
            Buffer.BlockCopy(b1, 0, result, count, count);
            Buffer.BlockCopy(b2, 0, result, count * 2, count);
            Buffer.BlockCopy(b3, 0, result, count * 3, count);
            return result;
        }

        public static List<RobloxChunk> ReadChunks(byte[] fileData)
        {
            return ReadChunksWithProgress(fileData, null);
        }

        public static List<RobloxChunk> ReadChunksWithProgress(byte[] fileData, Action<string, double>? onProgress)
        {
            var chunks = new List<RobloxChunk>(4096);
            int pos = 32;
            int len = fileData.Length;
            int chunkIndex = 0;

            while (pos + 16 <= len)
            {
                string chunkName = Encoding.Latin1.GetString(fileData, pos, 4);
                uint compLen = BitConverter.ToUInt32(fileData, pos + 4);
                uint uncompLen = BitConverter.ToUInt32(fileData, pos + 8);
                uint reserved = BitConverter.ToUInt32(fileData, pos + 12);
                pos += 16;

                int readLen = compLen > 0 ? (int)compLen : (int)uncompLen;
                byte[] rawChunkBytes = new byte[readLen];
                Buffer.BlockCopy(fileData, pos, rawChunkBytes, 0, readLen);
                pos += readLen;

                byte[] uncompData;
                if (compLen > 0)
                {
                    uncompData = Lz4BlockCodec.Decompress(rawChunkBytes, (int)uncompLen);
                }
                else
                {
                    uncompData = rawChunkBytes;
                }

                chunks.Add(new RobloxChunk
                {
                    Name = chunkName,
                    Data = uncompData,
                    RawCompressedData = rawChunkBytes,
                    CompLen = compLen,
                    UncompLen = uncompLen,
                    Reserved = reserved,
                    IsModified = false
                });

                chunkIndex++;
                if (chunkIndex % 600 == 0)
                {
                    double progress = 0.15 + 0.50 * ((double)pos / len);
                    onProgress?.Invoke($"Descomprimiendo chunks LZ4 ({chunkIndex:N0})...", progress);
                }

                if (chunkName.StartsWith("END"))
                    break;
            }

            return chunks;
        }

        public static byte[] RebuildFile(List<RobloxChunk> chunks, int classCount, int instanceCount)
        {
            using var ms = new MemoryStream();

            // Exact 32-byte Roblox Binary Header
            byte[] header = new byte[32];
            Buffer.BlockCopy(RobloxMagic, 0, header, 0, 14);
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)0), 0, header, 14, 2);       // Version
            Buffer.BlockCopy(BitConverter.GetBytes((uint)classCount), 0, header, 16, 4); // ClassCount
            Buffer.BlockCopy(BitConverter.GetBytes((uint)instanceCount), 0, header, 20, 4); // InstanceCount
            // Bytes 24..32: 8 zeros reserved

            ms.Write(header, 0, 32);

            foreach (var ch in chunks)
            {
                byte[] nameBytes = Encoding.Latin1.GetBytes(ch.Name.PadRight(4, '\0').Substring(0, 4));

                if (!ch.IsModified && ch.RawCompressedData != null && ch.RawCompressedData.Length > 0)
                {
                    // Preservación Quirúrgica 100% Exacta Byte-por-Byte de los datos originales
                    ms.Write(nameBytes, 0, 4);
                    ms.Write(BitConverter.GetBytes(ch.CompLen), 0, 4);
                    ms.Write(BitConverter.GetBytes(ch.UncompLen), 0, 4);
                    ms.Write(BitConverter.GetBytes(ch.Reserved), 0, 4);
                    ms.Write(ch.RawCompressedData, 0, ch.RawCompressedData.Length);
                }
                else
                {
                    // Chunk nuevo o modificado
                    byte[] uncompData = ch.Data;
                    uint uncompLen = (uint)uncompData.Length;

                    if (uncompLen < 32)
                    {
                        ms.Write(nameBytes, 0, 4);
                        ms.Write(BitConverter.GetBytes(0u), 0, 4);
                        ms.Write(BitConverter.GetBytes(uncompLen), 0, 4);
                        ms.Write(BitConverter.GetBytes(0u), 0, 4);
                        ms.Write(uncompData, 0, uncompData.Length);
                    }
                    else
                    {
                        byte[] compData = Lz4BlockCodec.Compress(uncompData);
                        if (compData.Length >= uncompLen)
                        {
                            ms.Write(nameBytes, 0, 4);
                            ms.Write(BitConverter.GetBytes(0u), 0, 4);
                            ms.Write(BitConverter.GetBytes(uncompLen), 0, 4);
                            ms.Write(BitConverter.GetBytes(0u), 0, 4);
                            ms.Write(uncompData, 0, uncompData.Length);
                        }
                        else
                        {
                            uint compLen = (uint)compData.Length;
                            ms.Write(nameBytes, 0, 4);
                            ms.Write(BitConverter.GetBytes(compLen), 0, 4);
                            ms.Write(BitConverter.GetBytes(uncompLen), 0, 4);
                            ms.Write(BitConverter.GetBytes(0u), 0, 4);
                            ms.Write(compData, 0, compData.Length);
                        }
                    }
                }
            }

            return ms.ToArray();
        }
    }
}
