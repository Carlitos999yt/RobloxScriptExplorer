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
                // Pure edit mode: surgically update ONLY Source and Name PROP chunks
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
                                resultChunks.Add(new RobloxChunk { Name = "PROP", Data = newChunkData });
                                continue;
                            }
                            else if (pname.Equals("Name", StringComparison.OrdinalIgnoreCase))
                            {
                                byte[] newChunkData = SerializeStringProp(cid, "Name", cinfo.InstanceIds, instances, "Name");
                                resultChunks.Add(new RobloxChunk { Name = "PROP", Data = newChunkData });
                                continue;
                            }
                        }
                    }

                    // Keep original chunk untouched
                    resultChunks.Add(ch);
                }

                return resultChunks;
            }

            // Instance count changed: update INST, PROP, PRNT for modified classes while keeping all other chunks
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
                        resultChunks.Add(new RobloxChunk { Name = "INST", Data = newInstData });
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
                            resultChunks.Add(new RobloxChunk { Name = "PROP", Data = newChunkData });
                            continue;
                        }
                        else if (ptype == 0x02) // Bool
                        {
                            byte[] newChunkData = SerializeBoolProp(cid, pname, cinfo.InstanceIds, instances, pname);
                            resultChunks.Add(new RobloxChunk { Name = "PROP", Data = newChunkData });
                            continue;
                        }
                        else
                        {
                            int perItem = GetPropertyItemSize(ptype, pname);
                            byte[] newChunkData = SerializeFixedProp(cid, pname, ptype, d, 9 + pnameLen, cinfo.InstanceIds.Count, perItem);
                            resultChunks.Add(new RobloxChunk { Name = "PROP", Data = newChunkData });
                            continue;
                        }
                    }
                }
                else if (ch.Name == "PRNT")
                {
                    byte[] newPrntData = SerializePrntChunk(instances);
                    resultChunks.Add(new RobloxChunk { Name = "PRNT", Data = newPrntData });
                    continue;
                }

                // Preserve untouched
                resultChunks.Add(ch);
            }

            return resultChunks;
        }

        private static byte[] SerializeStringProp(uint cid, string pname, List<int> instanceIds, Dictionary<int, RobloxInstance> instances, string propName)
        {
            byte[] pnameBytes = Encoding.UTF8.GetBytes(pname);
            using var ms = new MemoryStream();
            ms.Write(BitConverter.GetBytes(cid), 0, 4);
            ms.Write(BitConverter.GetBytes((uint)pnameBytes.Length), 0, 4);
            ms.Write(pnameBytes, 0, pnameBytes.Length);
            ms.WriteByte(0x01);

            foreach (int id in instanceIds)
            {
                string val = string.Empty;
                if (instances.TryGetValue(id, out var inst))
                {
                    if (propName.Equals("Name", StringComparison.OrdinalIgnoreCase))
                    {
                        val = inst.Name;
                    }
                    else if (inst.Properties.TryGetValue(propName, out var pval))
                    {
                        val = pval ?? string.Empty;
                    }
                }
                byte[] strBytes = Encoding.UTF8.GetBytes(val);
                ms.Write(BitConverter.GetBytes((uint)strBytes.Length), 0, 4);
                ms.Write(strBytes, 0, strBytes.Length);
            }

            return ms.ToArray();
        }

        private static byte[] SerializeBoolProp(uint cid, string pname, List<int> instanceIds, Dictionary<int, RobloxInstance> instances, string propName)
        {
            byte[] pnameBytes = Encoding.UTF8.GetBytes(pname);
            using var ms = new MemoryStream();
            ms.Write(BitConverter.GetBytes(cid), 0, 4);
            ms.Write(BitConverter.GetBytes((uint)pnameBytes.Length), 0, 4);
            ms.Write(pnameBytes, 0, pnameBytes.Length);
            ms.WriteByte(0x02);

            foreach (int id in instanceIds)
            {
                bool b = false;
                if (instances.TryGetValue(id, out var inst) && inst.Properties.TryGetValue(propName, out var pval))
                {
                    b = pval.Equals("true", StringComparison.OrdinalIgnoreCase) || pval == "1";
                }
                ms.WriteByte(b ? (byte)1 : (byte)0);
            }

            return ms.ToArray();
        }

        private static byte[] SerializeFixedProp(uint cid, string pname, byte ptype, byte[] origData, int origOffset, int newCount, int perItem)
        {
            byte[] pnameBytes = Encoding.UTF8.GetBytes(pname);
            using var ms = new MemoryStream();
            ms.Write(BitConverter.GetBytes(cid), 0, 4);
            ms.Write(BitConverter.GetBytes((uint)pnameBytes.Length), 0, 4);
            ms.Write(pnameBytes, 0, pnameBytes.Length);
            ms.WriteByte(ptype);

            int origDataLen = origData.Length - origOffset;
            int neededBytes = newCount * perItem;
            byte[] scaled = new byte[neededBytes];
            int copyBytes = Math.Min(origDataLen, neededBytes);
            if (copyBytes > 0)
            {
                Buffer.BlockCopy(origData, origOffset, scaled, 0, copyBytes);
            }
            ms.Write(scaled, 0, scaled.Length);

            return ms.ToArray();
        }

        private static byte[] SerializeInstChunk(RobloxClassInfo cinfo)
        {
            int count = cinfo.InstanceIds.Count;
            int[] deltas = new int[count];
            int prev = 0;
            for (int i = 0; i < count; i++)
            {
                deltas[i] = cinfo.InstanceIds[i] - prev;
                prev = cinfo.InstanceIds[i];
            }

            byte[] encDeltas = RobloxBinaryFormat.EncodeIntArray(deltas);

            using var ms = new MemoryStream();
            ms.Write(BitConverter.GetBytes(cinfo.ClassId), 0, 4);
            byte[] cnameBytes = Encoding.UTF8.GetBytes(cinfo.Name);
            ms.Write(BitConverter.GetBytes((uint)cnameBytes.Length), 0, 4);
            ms.Write(cnameBytes, 0, cnameBytes.Length);
            ms.WriteByte(cinfo.IsService ? (byte)1 : (byte)0);
            ms.Write(BitConverter.GetBytes((uint)count), 0, 4);
            ms.Write(encDeltas, 0, encDeltas.Length);

            if (cinfo.IsService)
            {
                for (int i = 0; i < count; i++) ms.WriteByte(1);
            }

            return ms.ToArray();
        }

        private static byte[] SerializePrntChunk(Dictionary<int, RobloxInstance> instances)
        {
            var pairs = instances.Values
                .Where(i => i.ParentId.HasValue && i.ParentId.Value != -1 && instances.ContainsKey(i.ParentId.Value))
                .Select(i => (ChildId: i.Id, ParentId: i.ParentId!.Value))
                .OrderBy(p => p.ChildId)
                .ToList();

            int count = pairs.Count;
            int[] childDeltas = new int[count];
            int[] parentDeltas = new int[count];
            int cPrev = 0;
            int pPrev = 0;
            for (int i = 0; i < count; i++)
            {
                childDeltas[i] = pairs[i].ChildId - cPrev;
                parentDeltas[i] = pairs[i].ParentId - pPrev;
                cPrev = pairs[i].ChildId;
                pPrev = pairs[i].ParentId;
            }

            byte[] encChild = RobloxBinaryFormat.EncodeIntArray(childDeltas);
            byte[] encParent = RobloxBinaryFormat.EncodeIntArray(parentDeltas);

            using var ms = new MemoryStream();
            ms.WriteByte(0);
            ms.Write(BitConverter.GetBytes((uint)count), 0, 4);
            ms.Write(encChild, 0, encChild.Length);
            ms.Write(encParent, 0, encParent.Length);

            return ms.ToArray();
        }

        private static int GetPropertyItemSize(byte ptype, string pname)
        {
            if (ptype == 0x02) return 1;
            if (ptype == 0x21 || pname.Equals("Capabilities", StringComparison.OrdinalIgnoreCase)) return 8;
            if (ptype == 0x1F || pname.Equals("UniqueId", StringComparison.OrdinalIgnoreCase) || pname.Equals("HistoryId", StringComparison.OrdinalIgnoreCase)) return 16;
            if (ptype == 0x1B || pname.Equals("SourceAssetId", StringComparison.OrdinalIgnoreCase)) return 8;
            if (ptype is 0x12 or 0x13 or 0x1C or 0x03 or 0x04) return 4;
            if (ptype == 0x05) return 8;
            if (ptype == 0x0E) return 12;
            if (ptype == 0x18) return 3;
            return 4;
        }
    }
}
