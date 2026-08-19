using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RobloxScriptExplorer.Logica
{
    public static class PropChunkHelper
    {
        public static List<RobloxChunk> BuildPreservedChunks(
            List<RobloxChunk> originalChunks,
            Dictionary<uint, RobloxClassInfo> classes,
            Dictionary<int, RobloxInstance> instances,
            int headerClassCount,
            int headerInstanceCount,
            bool hasAddedOrDeletedInstances)
        {
            var resultChunks = new List<RobloxChunk>();

            if (!hasAddedOrDeletedInstances)
            {
                // Modo Edición Pura: Preserva el 100% de chunks intactos y solo actualiza los PROP modificados
                foreach (var ch in originalChunks)
                {
                    if (ch.Name == "PROP")
                    {
                        byte[] d = ch.Data;
                        uint cid = BitConverter.ToUInt32(d, 0);
                        int pnameLen = (int)BitConverter.ToUInt32(d, 4);
                        string pname = Encoding.UTF8.GetString(d, 8, pnameLen);
                        byte ptype = d[8 + pnameLen];

                        if (classes.TryGetValue(cid, out var cinfo) && ptype == 0x01)
                        {
                            if (pname.Equals("Source", StringComparison.OrdinalIgnoreCase))
                            {
                                byte[] newChunkData = SerializeStringProp(cid, "Source", cinfo.InstanceIds, instances, "Source");
                                if (!newChunkData.SequenceEqual(ch.Data))
                                {
                                    resultChunks.Add(new RobloxChunk { Name = "PROP", Data = newChunkData, IsModified = true });
                                    continue;
                                }
                            }
                            else if (pname.Equals("Name", StringComparison.OrdinalIgnoreCase))
                            {
                                byte[] newChunkData = SerializeStringProp(cid, "Name", cinfo.InstanceIds, instances, "Name");
                                if (!newChunkData.SequenceEqual(ch.Data))
                                {
                                    resultChunks.Add(new RobloxChunk { Name = "PROP", Data = newChunkData, IsModified = true });
                                    continue;
                                }
                            }
                        }
                    }

                    // Mantener chunk original byte-por-byte idéntico
                    resultChunks.Add(ch);
                }

                return resultChunks;
            }

            // Se agregaron o eliminaron instancias
            var updatedProps = new HashSet<string>();

            foreach (var ch in originalChunks)
            {
                if (ch.Name == "INST")
                {
                    byte[] d = ch.Data;
                    uint cid = BitConverter.ToUInt32(d, 0);

                    if (classes.TryGetValue(cid, out var cinfo) && cinfo.Name is "Script" or "LocalScript" or "ModuleScript" or "Folder")
                    {
                        byte[] newInstData = SerializeInstChunk(cinfo);
                        resultChunks.Add(new RobloxChunk { Name = "INST", Data = newInstData, IsModified = true });
                        continue;
                    }
                }
                else if (ch.Name == "PROP")
                {
                    byte[] d = ch.Data;
                    uint cid = BitConverter.ToUInt32(d, 0);
                    int pnameLen = (int)BitConverter.ToUInt32(d, 4);
                    string pname = Encoding.UTF8.GetString(d, 8, pnameLen);
                    byte ptype = d[8 + pnameLen];

                    if (classes.TryGetValue(cid, out var cinfo) && cinfo.Name is "Script" or "LocalScript" or "ModuleScript" or "Folder")
                    {
                        string propKey = $"{cid}:{pname}";
                        updatedProps.Add(propKey);

                        if (ptype == 0x01) // String
                        {
                            byte[] newChunkData = SerializeStringProp(cid, pname, cinfo.InstanceIds, instances, pname);
                            resultChunks.Add(new RobloxChunk { Name = "PROP", Data = newChunkData, IsModified = true });
                            continue;
                        }
                        else if (ptype == 0x02) // Bool
                        {
                            byte[] newChunkData = SerializeBoolProp(cid, pname, cinfo.InstanceIds, instances, pname);
                            resultChunks.Add(new RobloxChunk { Name = "PROP", Data = newChunkData, IsModified = true });
                            continue;
                        }
                        else
                        {
                            byte[] newChunkData = PadRawPropData(d, pnameLen, ptype, cinfo.InstanceIds.Count);
                            resultChunks.Add(new RobloxChunk { Name = "PROP", Data = newChunkData, IsModified = true });
                            continue;
                        }
                    }
                }
                else if (ch.Name == "PRNT")
                {
                    byte[] newPrntData = SerializePrntChunk(classes, instances);
                    resultChunks.Add(new RobloxChunk { Name = "PRNT", Data = newPrntData, IsModified = true });
                    continue;
                }

                // Chunk no modificado se preserva 100%
                resultChunks.Add(ch);
            }

            // Añadir INST/PROP faltantes para nuevas clases
            foreach (var kvp in classes.OrderBy(c => c.Key))
            {
                uint cid = kvp.Key;
                var cinfo = kvp.Value;
                if (!originalChunks.Any(c => c.Name == "INST" && BitConverter.ToUInt32(c.Data, 0) == cid))
                {
                    int prntIndex = resultChunks.FindIndex(c => c.Name == "PRNT");
                    if (prntIndex == -1) prntIndex = resultChunks.Count;

                    resultChunks.Insert(prntIndex, new RobloxChunk { Name = "INST", Data = SerializeInstChunk(cinfo), IsModified = true });

                    byte[] nameProp = SerializeStringProp(cid, "Name", cinfo.InstanceIds, instances, "Name");
                    resultChunks.Insert(prntIndex + 1, new RobloxChunk { Name = "PROP", Data = nameProp, IsModified = true });

                    if (cinfo.Name is "Script" or "LocalScript" or "ModuleScript")
                    {
                        byte[] srcProp = SerializeStringProp(cid, "Source", cinfo.InstanceIds, instances, "Source");
                        resultChunks.Insert(prntIndex + 2, new RobloxChunk { Name = "PROP", Data = srcProp, IsModified = true });
                    }
                }
            }

            return resultChunks;
        }

        private static byte[] SerializeInstChunk(RobloxClassInfo cinfo)
        {
            using var ms = new MemoryStream();
            ms.Write(BitConverter.GetBytes(cinfo.ClassId), 0, 4);

            byte[] cnameBytes = Encoding.UTF8.GetBytes(cinfo.Name);
            ms.Write(BitConverter.GetBytes((uint)cnameBytes.Length), 0, 4);
            ms.Write(cnameBytes, 0, cnameBytes.Length);

            ms.WriteByte((byte)(cinfo.IsService ? 1 : 0));

            int count = cinfo.InstanceIds.Count;
            ms.Write(BitConverter.GetBytes((uint)count), 0, 4);

            int[] deltas = new int[count];
            int prev = 0;
            for (int i = 0; i < count; i++)
            {
                deltas[i] = cinfo.InstanceIds[i] - prev;
                prev = cinfo.InstanceIds[i];
            }

            byte[] encodedDeltas = RobloxBinaryFormat.EncodeIntArray(deltas);
            ms.Write(encodedDeltas, 0, encodedDeltas.Length);

            return ms.ToArray();
        }

        private static byte[] SerializeStringProp(uint classId, string propName, List<int> instanceIds, Dictionary<int, RobloxInstance> instances, string propKey)
        {
            using var ms = new MemoryStream();
            ms.Write(BitConverter.GetBytes(classId), 0, 4);

            byte[] pnameBytes = Encoding.UTF8.GetBytes(propName);
            ms.Write(BitConverter.GetBytes((uint)pnameBytes.Length), 0, 4);
            ms.Write(pnameBytes, 0, pnameBytes.Length);

            ms.WriteByte(0x01); // String Type

            foreach (int id in instanceIds)
            {
                string val = string.Empty;
                if (instances.TryGetValue(id, out var inst) && inst.Properties.TryGetValue(propKey, out var sval))
                {
                    val = sval ?? string.Empty;
                }

                byte[] valBytes = Encoding.UTF8.GetBytes(val);
                ms.Write(BitConverter.GetBytes((uint)valBytes.Length), 0, 4);
                ms.Write(valBytes, 0, valBytes.Length);
            }

            return ms.ToArray();
        }

        private static byte[] SerializeBoolProp(uint classId, string propName, List<int> instanceIds, Dictionary<int, RobloxInstance> instances, string propKey)
        {
            using var ms = new MemoryStream();
            ms.Write(BitConverter.GetBytes(classId), 0, 4);

            byte[] pnameBytes = Encoding.UTF8.GetBytes(propName);
            ms.Write(BitConverter.GetBytes((uint)pnameBytes.Length), 0, 4);
            ms.Write(pnameBytes, 0, pnameBytes.Length);

            ms.WriteByte(0x02); // Bool Type

            foreach (int id in instanceIds)
            {
                byte val = 0;
                if (instances.TryGetValue(id, out var inst) && inst.Properties.TryGetValue(propKey, out var sval))
                {
                    if (bool.TryParse(sval, out bool b) && b) val = 1;
                }
                ms.WriteByte(val);
            }

            return ms.ToArray();
        }

        private static byte[] PadRawPropData(byte[] originalData, int pnameLen, byte ptype, int newCount)
        {
            int origHeaderLen = 9 + pnameLen;
            int origPayloadLen = originalData.Length - origHeaderLen;
            int bytesPerElem = 4;

            if (ptype == 0x02) bytesPerElem = 1;
            else if (ptype == 0x03) bytesPerElem = 4;
            else if (ptype == 0x04) bytesPerElem = 4;
            else if (ptype == 0x05) bytesPerElem = 8;
            else if (ptype == 0x06) bytesPerElem = 12;

            int neededPayloadLen = newCount * bytesPerElem;
            using var ms = new MemoryStream(origHeaderLen + neededPayloadLen);
            ms.Write(originalData, 0, origHeaderLen);

            if (origPayloadLen > 0)
            {
                ms.Write(originalData, origHeaderLen, Math.Min(origPayloadLen, neededPayloadLen));
            }

            int diff = neededPayloadLen - origPayloadLen;
            if (diff > 0)
            {
                ms.Write(new byte[diff], 0, diff);
            }

            return ms.ToArray();
        }

        private static byte[] SerializePrntChunk(Dictionary<uint, RobloxClassInfo> classes, Dictionary<int, RobloxInstance> instances)
        {
            var pairs = new List<(int Child, int Parent)>();

            foreach (var inst in instances.Values.OrderBy(i => i.Id))
            {
                if (inst.ParentId.HasValue && inst.ParentId.Value != 0)
                {
                    pairs.Add((inst.Id, inst.ParentId.Value));
                }
            }

            int count = pairs.Count;
            using var ms = new MemoryStream();
            ms.WriteByte(0x00); // PRNT version
            ms.Write(BitConverter.GetBytes((uint)count), 0, 4);

            int[] childDeltas = new int[count];
            int[] parentDeltas = new int[count];
            int prevChild = 0;
            int prevParent = 0;

            for (int i = 0; i < count; i++)
            {
                childDeltas[i] = pairs[i].Child - prevChild;
                prevChild = pairs[i].Child;

                parentDeltas[i] = pairs[i].Parent - prevParent;
                prevParent = pairs[i].Parent;
            }

            byte[] encChild = RobloxBinaryFormat.EncodeIntArray(childDeltas);
            byte[] encParent = RobloxBinaryFormat.EncodeIntArray(parentDeltas);

            ms.Write(encChild, 0, encChild.Length);
            ms.Write(encParent, 0, encParent.Length);

            return ms.ToArray();
        }
    }
}
