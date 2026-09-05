using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RobloxScriptExplorer.Logica
{
    /// <summary>
    /// Motor de alto nivel para cargar, editar, crear scripts, exportar y guardar archivos de Roblox Place (.rbxl)
    /// con Preservación Quirúrgica del 100% de Chunks intactos.
    /// </summary>
    public class RbxlPlaceManager
    {
        public string FilePath { get; private set; } = string.Empty;
        public List<RobloxChunk> Chunks { get; private set; } = new();
        public Dictionary<uint, RobloxClassInfo> Classes { get; } = new();
        public Dictionary<int, RobloxInstance> Instances { get; } = new();
        public HashSet<int> ModifiedInstanceIds { get; } = new();
        public int HeaderClassCount { get; private set; } = 0;
        public int HeaderInstanceCount { get; private set; } = 0;
        public bool IsLoaded => Instances.Count > 0;

        private bool _hasModifiedStructure = false;

        private static readonly HashSet<string> RelevantClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Script", "LocalScript", "ModuleScript",
            "Folder", "Configuration", "Model",
            "ScreenGui", "Frame", "TextLabel", "TextButton", "ImageLabel", "ImageButton", "TextBox",
            "RemoteFunction", "RemoteEvent", "BindableFunction", "BindableEvent",
            "StarterPlayerScripts", "StarterCharacterScripts", "Sound", "SoundGroup",
            "Lighting", "SoundService", "Chat", "Players", "Workspace",
            "ServerScriptService", "ServerStorage", "ReplicatedStorage", "ReplicatedFirst", "StarterGui", "StarterPlayer",
            "Part", "MeshPart", "SpecialMesh", "Decal", "Texture", "Attachment", "Weld", "Motor6D", "SpawnLocation", "Camera"
        };

        public async Task LoadAsync(string filePath, Action<string, double>? onProgress = null)
        {
            FilePath = filePath;
            _hasModifiedStructure = false;
            ModifiedInstanceIds.Clear();
            onProgress?.Invoke("Leyendo archivo de disco...", 0.05);

            byte[] rawData = await File.ReadAllBytesAsync(filePath);

            if (rawData.Length < 32 || !rawData.Take(8).SequenceEqual(Encoding.Latin1.GetBytes("<roblox!")))
            {
                throw new NotSupportedException("El archivo no es un binario .rbxl válido o es un archivo XML.");
            }

            HeaderClassCount = (int)BitConverter.ToUInt32(rawData, 16);
            HeaderInstanceCount = (int)BitConverter.ToUInt32(rawData, 20);

            onProgress?.Invoke("Descomprimiendo chunks binarios LZ4...", 0.20);
            Chunks = await Task.Run(() => RobloxBinaryFormat.ReadChunksWithProgress(rawData, onProgress));

            rawData = Array.Empty<byte>();

            onProgress?.Invoke("Procesando jerarquía e instancias...", 0.75);
            await Task.Run(() => ParseInstances(onProgress));

            MemoryOptimizer.TrimMemory();

            onProgress?.Invoke("¡Carga completada!", 1.0);
        }

        private void ParseInstances(Action<string, double>? onProgress)
        {
            Classes.Clear();
            Instances.Clear();

            var relevantClassIds = new HashSet<uint>();

            foreach (var ch in Chunks)
            {
                if (ch.Name == "INST")
                {
                    byte[] d = ch.Data;
                    uint cid = BitConverter.ToUInt32(d, 0);
                    int cnameLen = (int)BitConverter.ToUInt32(d, 4);
                    string cname = Encoding.UTF8.GetString(d, 8, cnameLen);
                    int offset = 8 + cnameLen;
                    bool isService = d[offset] != 0;
                    offset += 1;
                    int count = (int)BitConverter.ToUInt32(d, offset);
                    offset += 4;

                    int[] deltas = RobloxBinaryFormat.DecodeIntArray(d, offset, count);
                    var classInfo = new RobloxClassInfo
                    {
                        ClassId = cid,
                        Name = cname,
                        IsService = isService,
                        Count = (uint)count
                    };

                    bool isRelevant = isService || RelevantClasses.Contains(cname);
                    if (isRelevant)
                    {
                        relevantClassIds.Add(cid);
                    }

                    int curr = 0;
                    foreach (int delta in deltas)
                    {
                        curr += delta;
                        classInfo.InstanceIds.Add(curr);

                        if (isRelevant)
                        {
                            Instances[curr] = new RobloxInstance
                            {
                                Id = curr,
                                ClassId = cid,
                                ClassName = cname,
                                Name = $"{cname}_{curr}",
                                IsService = isService
                            };
                        }
                    }

                    Classes[cid] = classInfo;
                }
            }

            // 2. PROP Chunks
            int propCount = 0;
            int totalPropChunks = Chunks.Count(c => c.Name == "PROP");
            foreach (var ch in Chunks)
            {
                if (ch.Name == "PROP")
                {
                    propCount++;
                    if (propCount % 600 == 0)
                    {
                        onProgress?.Invoke($"Procesando propiedades ({propCount:N0} / {totalPropChunks:N0})...", 0.75 + 0.15 * (propCount / (double)totalPropChunks));
                    }

                    byte[] d = ch.Data;
                    uint cid = BitConverter.ToUInt32(d, 0);

                    if (!relevantClassIds.Contains(cid))
                        continue;

                    int pnameLen = (int)BitConverter.ToUInt32(d, 4);
                    string pname = Encoding.UTF8.GetString(d, 8, pnameLen);
                    byte ptype = d[8 + pnameLen];
                    int pdataOffset = 9 + pnameLen;

                    if (Classes.TryGetValue(cid, out var cinfo))
                    {
                        int count = cinfo.InstanceIds.Count;

                        // 1. Strings (0x01)
                        if (ptype == 0x01)
                        {
                            int spos = pdataOffset;
                            for (int i = 0; i < count; i++)
                            {
                                if (spos + 4 <= d.Length)
                                {
                                    int slen = (int)BitConverter.ToUInt32(d, spos);
                                    spos += 4;
                                    if (spos + slen <= d.Length)
                                    {
                                        string sval = Encoding.UTF8.GetString(d, spos, slen);
                                        spos += slen;

                                        int iId = cinfo.InstanceIds[i];
                                        if (Instances.TryGetValue(iId, out var inst))
                                        {
                                            inst.Properties[pname] = sval;
                                            if (pname.Equals("Name", StringComparison.OrdinalIgnoreCase))
                                            {
                                                inst.Name = sval;
                                            }
                                            else if (pname is "Texture" or "MeshId" or "TextureID" or "TextureId" or "Image" or "HoverImage" or "PressedImage" or "SoundId" or "AnimationId" or "PantsTemplate" or "ShirtTemplate" or "Graphic" or "ColorMap" or "MetalnessMap" or "NormalMap" or "RoughnessMap" or "SkyboxBk" or "SkyboxDn" or "SkyboxFt" or "SkyboxLf" or "SkyboxRt" or "SkyboxUp" or "SunTextureId" or "MoonTextureId" or "Video"
                                                     || ((sval.StartsWith("rbxasset", StringComparison.OrdinalIgnoreCase) || sval.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || sval.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) && pname != "Source" && pname != "LinkedSource"))
                                            {
                                                if (!string.IsNullOrWhiteSpace(sval))
                                                    inst.XmlProperties[pname] = $"<Content name=\"{pname}\"><url>{EscapeXml(sval)}</url></Content>";
                                                else
                                                    inst.XmlProperties[pname] = $"<Content name=\"{pname}\"><null></null></Content>";
                                            }
                                            else if (pname != "AttributesSerialize" && pname != "PhysicsData")
                                            {
                                                inst.XmlProperties[pname] = $"<string name=\"{pname}\">{EscapeXml(SanitizeForXml(sval))}</string>";
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        // 2. Bool (0x02)
                        else if (ptype == 0x02 && pdataOffset + count <= d.Length)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                if (Instances.TryGetValue(cinfo.InstanceIds[i], out var inst))
                                {
                                    bool bval = d[pdataOffset + i] != 0;
                                    inst.Properties[pname] = bval.ToString().ToLowerInvariant();
                                    inst.XmlProperties[pname] = $"<bool name=\"{pname}\">{bval.ToString().ToLowerInvariant()}</bool>";
                                }
                            }
                        }
                        // 3. Int32 (0x03)
                        else if (ptype == 0x03 && pdataOffset + count * 4 <= d.Length)
                        {
                            var ints = DecodeInt32Array(d, pdataOffset, count);
                            for (int i = 0; i < count; i++)
                            {
                                if (Instances.TryGetValue(cinfo.InstanceIds[i], out var inst))
                                {
                                    inst.Properties[pname] = ints[i].ToString();
                                    inst.XmlProperties[pname] = $"<int name=\"{pname}\">{ints[i]}</int>";
                                }
                            }
                        }
                        // 4. Float32 (0x04)
                        else if (ptype == 0x04 && pdataOffset + count * 4 <= d.Length)
                        {
                            var floats = DecodeFloatArray(d, pdataOffset, count);
                            for (int i = 0; i < count; i++)
                            {
                                if (Instances.TryGetValue(cinfo.InstanceIds[i], out var inst))
                                {
                                    float fval = floats[i];
                                    inst.Properties[pname] = fval.ToString("G9", CultureInfo.InvariantCulture);
                                    inst.XmlProperties[pname] = $"<float name=\"{pname}\">{fval.ToString("G9", CultureInfo.InvariantCulture)}</float>";
                                }
                            }
                        }
                        // 5. UDim2 (0x07)
                        else if (ptype == 0x07 && pdataOffset + count * 16 <= d.Length)
                        {
                            float[] sxs = DecodeFloatArray(d, pdataOffset + 0 * count * 4, count);
                            float[] sys = DecodeFloatArray(d, pdataOffset + 1 * count * 4, count);
                            int[] oxs = DecodeInt32Array(d, pdataOffset + 2 * count * 4, count);
                            int[] oys = DecodeInt32Array(d, pdataOffset + 3 * count * 4, count);
                            for (int i = 0; i < count; i++)
                            {
                                if (Instances.TryGetValue(cinfo.InstanceIds[i], out var inst))
                                {
                                    inst.XmlProperties[pname] = $"<UDim2 name=\"{pname}\"><XS>{sxs[i].ToString("G9", CultureInfo.InvariantCulture)}</XS><XO>{oxs[i]}</XO><YS>{sys[i].ToString("G9", CultureInfo.InvariantCulture)}</YS><YO>{oys[i]}</YO></UDim2>";
                                }
                            }
                        }
                        // 6. Color3 float RGB (0x0C)
                        else if (ptype == 0x0C && pdataOffset + count * 12 <= d.Length)
                        {
                            float[] rs = DecodeFloatArray(d, pdataOffset + 0 * count * 4, count);
                            float[] gs = DecodeFloatArray(d, pdataOffset + 1 * count * 4, count);
                            float[] bs = DecodeFloatArray(d, pdataOffset + 2 * count * 4, count);
                            for (int i = 0; i < count; i++)
                            {
                                if (Instances.TryGetValue(cinfo.InstanceIds[i], out var inst))
                                {
                                    inst.XmlProperties[pname] = $"<Color3 name=\"{pname}\"><R>{rs[i].ToString("G9", CultureInfo.InvariantCulture)}</R><G>{gs[i].ToString("G9", CultureInfo.InvariantCulture)}</G><B>{bs[i].ToString("G9", CultureInfo.InvariantCulture)}</B></Color3>";
                                }
                            }
                        }
                        // 7. Vector2 (0x0D)
                        else if (ptype == 0x0D && pdataOffset + count * 8 <= d.Length)
                        {
                            float[] xs = DecodeFloatArray(d, pdataOffset + 0 * count * 4, count);
                            float[] ys = DecodeFloatArray(d, pdataOffset + 1 * count * 4, count);
                            for (int i = 0; i < count; i++)
                            {
                                if (Instances.TryGetValue(cinfo.InstanceIds[i], out var inst))
                                {
                                    inst.XmlProperties[pname] = $"<Vector2 name=\"{pname}\"><X>{xs[i].ToString("G9", CultureInfo.InvariantCulture)}</X><Y>{ys[i].ToString("G9", CultureInfo.InvariantCulture)}</Y></Vector2>";
                                }
                            }
                        }
                        // 8. Vector3 (0x0E): size, Scale, Offset, InitialSize, ModelMeshSize, VertexColor
                        else if (ptype == 0x0E && pdataOffset + count * 12 <= d.Length)
                        {
                            var v3s = DecodeVector3Array(d, pdataOffset, count);
                            for (int i = 0; i < count; i++)
                            {
                                if (Instances.TryGetValue(cinfo.InstanceIds[i], out var inst))
                                {
                                    var v = v3s[i];
                                    inst.Properties[pname] = $"({v.X:F3}, {v.Y:F3}, {v.Z:F3})";
                                    inst.XmlProperties[pname] = $"<Vector3 name=\"{pname}\"><X>{v.X.ToString("G9", CultureInfo.InvariantCulture)}</X><Y>{v.Y.ToString("G9", CultureInfo.InvariantCulture)}</Y><Z>{v.Z.ToString("G9", CultureInfo.InvariantCulture)}</Z></Vector3>";
                                }
                            }
                        }
                        // 9. CoordinateFrame (0x10): CFrame, PivotOffset, ModelMeshCFrame, C0, C1
                        else if (ptype == 0x10)
                        {
                            var cframes = DecodeCFrameArray(d, pdataOffset, count);
                            for (int i = 0; i < count; i++)
                            {
                                if (Instances.TryGetValue(cinfo.InstanceIds[i], out var inst))
                                {
                                    var cf = cframes[i];
                                    inst.Properties[pname] = $"({cf.X:F2}, {cf.Y:F2}, {cf.Z:F2})";
                                    inst.XmlProperties[pname] = $"<CoordinateFrame name=\"{pname}\"><X>{cf.X.ToString("G9", CultureInfo.InvariantCulture)}</X><Y>{cf.Y.ToString("G9", CultureInfo.InvariantCulture)}</Y><Z>{cf.Z.ToString("G9", CultureInfo.InvariantCulture)}</Z><R00>{cf.R00.ToString("G9", CultureInfo.InvariantCulture)}</R00><R01>{cf.R01.ToString("G9", CultureInfo.InvariantCulture)}</R01><R02>{cf.R02.ToString("G9", CultureInfo.InvariantCulture)}</R02><R10>{cf.R10.ToString("G9", CultureInfo.InvariantCulture)}</R10><R11>{cf.R11.ToString("G9", CultureInfo.InvariantCulture)}</R11><R12>{cf.R12.ToString("G9", CultureInfo.InvariantCulture)}</R12><R20>{cf.R20.ToString("G9", CultureInfo.InvariantCulture)}</R20><R21>{cf.R21.ToString("G9", CultureInfo.InvariantCulture)}</R21><R22>{cf.R22.ToString("G9", CultureInfo.InvariantCulture)}</R22></CoordinateFrame>";
                                }
                            }
                        }
                        // 10. Enum / Token (0x12): Material, shape, formFactorRaw, MeshType, etc.
                        else if (ptype == 0x12 && pdataOffset + count * 4 <= d.Length)
                        {
                            var enums = DecodeUintArray(d, pdataOffset, count);
                            for (int i = 0; i < count; i++)
                            {
                                if (Instances.TryGetValue(cinfo.InstanceIds[i], out var inst))
                                {
                                    inst.Properties[pname] = enums[i].ToString();
                                    inst.XmlProperties[pname] = $"<token name=\"{pname}\">{enums[i]}</token>";
                                }
                            }
                        }
                        // 11. Referent (0x13): PrimaryPart, Part0, Part1, SoundGroup
                        else if (ptype == 0x13 && pdataOffset + count * 4 <= d.Length)
                        {
                            var refs = DecodeReferentArray(d, pdataOffset, count);
                            for (int i = 0; i < count; i++)
                            {
                                if (Instances.TryGetValue(cinfo.InstanceIds[i], out var inst))
                                {
                                    int targetRef = refs[i];
                                    inst.XmlProperties[pname] = targetRef >= 0 ? $"<Ref name=\"{pname}\">RBX{targetRef}</Ref>" : $"<Ref name=\"{pname}\">null</Ref>";
                                }
                            }
                        }
                        // 12. Color3uint8 (0x1A)
                        else if (ptype == 0x1A && pdataOffset + count * 3 <= d.Length)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                if (Instances.TryGetValue(cinfo.InstanceIds[i], out var inst))
                                {
                                    byte r = d[pdataOffset + 0 * count + i];
                                    byte g = d[pdataOffset + 1 * count + i];
                                    byte b = d[pdataOffset + 2 * count + i];
                                    uint col = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
                                    inst.Properties["Color3uint8"] = col.ToString();
                                    inst.XmlProperties[pname] = $"<Color3uint8 name=\"{pname}\">{col}</Color3uint8>";
                                }
                            }
                        }
                        // 13. Int64 (0x1B)
                        else if (ptype == 0x1B && pdataOffset + count * 8 <= d.Length)
                        {
                            var i64s = DecodeInt64Array(d, pdataOffset, count);
                            for (int i = 0; i < count; i++)
                            {
                                if (Instances.TryGetValue(cinfo.InstanceIds[i], out var inst))
                                {
                                    inst.XmlProperties[pname] = $"<int64 name=\"{pname}\">{i64s[i]}</int64>";
                                }
                            }
                        }
                    }
                }
            }

            // 3. PRNT Chunks
            onProgress?.Invoke("Enlazando jerarquía...", 0.92);
            foreach (var ch in Chunks)
            {
                if (ch.Name == "PRNT")
                {
                    byte[] d = ch.Data;
                    int count = (int)BitConverter.ToUInt32(d, 1);
                    int[] childDeltas = RobloxBinaryFormat.DecodeIntArray(d, 5, count);
                    int[] parentDeltas = RobloxBinaryFormat.DecodeIntArray(d, 5 + 4 * count, count);

                    int childCurr = 0;
                    int parentCurr = 0;
                    for (int i = 0; i < count; i++)
                    {
                        childCurr += childDeltas[i];
                        parentCurr += parentDeltas[i];

                        if (Instances.TryGetValue(childCurr, out var childInst))
                        {
                            childInst.ParentId = parentCurr;
                            if (Instances.TryGetValue(parentCurr, out var parentInst))
                            {
                                parentInst.ChildrenIds.Add(childCurr);
                            }
                        }
                    }
                }
            }
        }

        public async Task<string> SaveAsync(string? targetPath = null, Action<string, double>? onProgress = null)
        {
            string outPath = targetPath ?? FilePath;
            string tempPath = outPath + ".tmp";
            string bakPath = outPath + ".bak";

            onProgress?.Invoke("Iniciando guardado...", 0.10);
            await Task.Delay(50);

            try
            {
                onProgress?.Invoke("Actualizando datos...", 0.40);
                
                List<RobloxChunk> preservedChunks;
                if (!_hasModifiedStructure && ModifiedInstanceIds.Count == 0)
                {
                    // Ningún cambio realizado: Preservación Quirúrgica 100% Intacta
                    preservedChunks = Chunks;
                }
                else
                {
                    preservedChunks = PropChunkHelper.BuildPreservedChunks(
                        Chunks, Classes, Instances, HeaderClassCount, HeaderInstanceCount, _hasModifiedStructure);
                }

                onProgress?.Invoke("Escribiendo datos binarios...", 0.75);
                byte[] newFileData = RobloxBinaryFormat.RebuildFile(preservedChunks, HeaderClassCount, HeaderInstanceCount);
                await File.WriteAllBytesAsync(tempPath, newFileData);

                newFileData = Array.Empty<byte>();

                onProgress?.Invoke("🧪 Verificando integridad binaria...", 0.92);
                await Task.Delay(100);
                VerifyRebuiltFile(tempPath);

                onProgress?.Invoke("🛡️ Verificación superada con 100% de éxito...", 0.98);
                await Task.Delay(50);

                if (File.Exists(outPath))
                {
                    File.Copy(outPath, bakPath, true);
                }
                File.Move(tempPath, outPath, true);

                MemoryOptimizer.TrimMemory();

                return bakPath;
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
                throw;
            }
        }

        public string Save(string? targetPath = null)
        {
            return SaveAsync(targetPath, null).GetAwaiter().GetResult();
        }

        private void VerifyRebuiltFile(string tempPath)
        {
            byte[] testData = File.ReadAllBytes(tempPath);

            if (testData.Length < 32 || !testData.Take(14).SequenceEqual(RobloxBinaryFormat.RobloxMagic))
            {
                throw new InvalidDataException("La firma de encabezado de Roblox Studio (<roblox!\\x89\\xff...) no coincide.");
            }

            var testChunks = RobloxBinaryFormat.ReadChunks(testData);
            if (testChunks.Count == 0 || !testChunks.Any(c => c.Name.StartsWith("END")))
            {
                throw new InvalidDataException("El archivo binario reconstruido está incompleto.");
            }
        }

        public RobloxInstance CreateScript(string name, string scriptType, int parentId)
        {
            var classInfo = Classes.Values.FirstOrDefault(c => c.Name.Equals(scriptType, StringComparison.OrdinalIgnoreCase));
            if (classInfo == null)
            {
                uint newClassId = Classes.Keys.Count > 0 ? Classes.Keys.Max() + 1 : 1;
                classInfo = new RobloxClassInfo
                {
                    ClassId = newClassId,
                    Name = scriptType,
                    IsService = false,
                    Count = 0
                };
                Classes[newClassId] = classInfo;
            }

            int newId = HeaderInstanceCount > 0 ? HeaderInstanceCount : (Instances.Keys.Count > 0 ? Instances.Keys.Max() + 1 : 1);
            var inst = new RobloxInstance
            {
                Id = newId,
                ClassId = classInfo.ClassId,
                ClassName = classInfo.Name,
                Name = name,
                ParentId = parentId,
                IsService = false
            };
            inst.Properties["Name"] = name;
            inst.Properties["Source"] = $"-- {name} ({scriptType})\nprint(\"Hola desde {name}!\")\n";

            Instances[newId] = inst;
            classInfo.InstanceIds.Add(newId);
            classInfo.Count++;
            HeaderInstanceCount++;
            _hasModifiedStructure = true;
            ModifiedInstanceIds.Add(newId);

            if (Instances.TryGetValue(parentId, out var parentInst))
            {
                parentInst.ChildrenIds.Add(newId);
            }

            return inst;
        }

        public bool DeleteInstance(int id)
        {
            if (!Instances.TryGetValue(id, out var inst) || inst.IsService)
                return false;

            if (inst.ParentId.HasValue && Instances.TryGetValue(inst.ParentId.Value, out var parentInst))
            {
                parentInst.ChildrenIds.Remove(id);
            }

            if (Classes.TryGetValue(inst.ClassId, out var classInfo))
            {
                classInfo.InstanceIds.Remove(id);
                if (classInfo.Count > 0) classInfo.Count--;
            }

            Instances.Remove(id);
            if (HeaderInstanceCount > 0) HeaderInstanceCount--;
            _hasModifiedStructure = true;
            ModifiedInstanceIds.Add(id);
            return true;
        }

        public string GetInstanceHierarchyPath(int id)
        {
            var parts = GetInstanceHierarchySegments(id);
            return string.Join("/", parts);
        }

        public List<string> GetInstanceHierarchySegments(int id)
        {
            var parts = new List<string>();
            int? curr = id;
            while (curr.HasValue && Instances.TryGetValue(curr.Value, out var inst))
            {
                parts.Add(inst.Name);
                curr = inst.ParentId;
            }
            parts.Reverse();
            return parts;
        }

        public string ExportAllInOneRbxmxPackages(string baseDirectory, Action<string, double>? onProgress = null)
        {
            string fileNameOnly = Path.GetFileNameWithoutExtension(FilePath);
            string projectDir = Path.Combine(baseDirectory, $"{fileNameOnly}_RobloxPackages_rbxmx");
            Directory.CreateDirectory(projectDir);

            var services = Instances.Values
                .Where(inst => inst.IsService && inst.ChildrenIds.Count > 0)
                .OrderBy(s => s.Name)
                .ToList();

            int total = services.Count;
            int count = 0;

            foreach (var svc in services)
            {
                count++;
                onProgress?.Invoke($"Exportando paquete {svc.Name}.rbxmx ({count}/{total})...", count / (double)total);

                string packagePath = Path.Combine(projectDir, $"{SanitizeFileName(svc.Name)}.rbxmx");
                ExportAsRbxmx(svc, packagePath);
            }

            var manifest = new
            {
                mode = "AllInOne_RobloxStudio_rbxmx",
                place_name = fileNameOnly,
                source_file = FilePath,
                export_date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                packages_exported = services.Select(s => $"{s.Name}.rbxmx").ToList()
            };
            File.WriteAllText(Path.Combine(projectDir, "packages_manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

            return projectDir;
        }

        public string ExportCompleteProject(string baseDirectory, Action<string, double>? onProgress = null)
        {
            string fileNameOnly = Path.GetFileNameWithoutExtension(FilePath);
            string projectDir = Path.Combine(baseDirectory, $"{fileNameOnly}_Modular_Exported");
            Directory.CreateDirectory(projectDir);

            int scriptCount = 0;
            int folderCount = 0;
            int modelCount = 0;

            var roots = Instances.Values
                .Where(inst => !inst.ParentId.HasValue || inst.ParentId.Value == -1 || inst.IsService || !Instances.ContainsKey(inst.ParentId.Value))
                .ToList();

            int totalRoots = roots.Count;
            int currentRoot = 0;

            foreach (var r in roots)
            {
                currentRoot++;
                onProgress?.Invoke($"Exportando nodo {r.Name} ({currentRoot}/{totalRoots})...", currentRoot / (double)totalRoots);
                ExportHierarchyNodeRecursive(r, projectDir, ref scriptCount, ref folderCount, ref modelCount);
            }

            var manifest = new
            {
                mode = "Modular_Luau_And_Models",
                place_name = fileNameOnly,
                source_file = FilePath,
                export_date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                total_instances = Instances.Count,
                total_scripts_exported = scriptCount,
                total_folders_exported = folderCount,
                total_models_exported = modelCount,
                services = Instances.Values.Where(i => i.IsService).Select(s => new {
                    name = s.Name,
                    children_count = s.ChildrenIds.Count
                })
            };

            string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(projectDir, "place_manifest.json"), manifestJson, Encoding.UTF8);

            return projectDir;
        }

        public void ExportHierarchyNodeRecursive(RobloxInstance inst, string parentDir, ref int scriptCount, ref int folderCount, ref int modelCount)
        {
            string currentDir = Path.Combine(parentDir, SanitizeFileName(inst.Name));

            if (inst.IsService || inst.ClassName is "Folder" or "ScreenGui" or "Model" or "StarterCharacterScripts" or "StarterPlayerScripts")
            {
                Directory.CreateDirectory(currentDir);
                folderCount++;

                if (inst.ClassName is "ScreenGui" or "Model" || inst.ChildrenIds.Count > 0)
                {
                    string rbxmxFile = Path.Combine(currentDir, $"{SanitizeFileName(inst.Name)}.rbxmx");
                    ExportAsRbxmx(inst, rbxmxFile);
                    modelCount++;
                }
            }

            if (inst.Properties.TryGetValue("Source", out string? src) && !string.IsNullOrEmpty(src))
            {
                Directory.CreateDirectory(parentDir);
                string scriptFile = Path.Combine(parentDir, $"{SanitizeFileName(inst.Name)}.{inst.ClassName}.luau");
                File.WriteAllText(scriptFile, src, Encoding.UTF8);
                scriptCount++;
            }

            foreach (int childId in inst.ChildrenIds)
            {
                if (Instances.TryGetValue(childId, out var child))
                {
                    string destDir = (inst.IsService || inst.ClassName is "Folder" or "ScreenGui" or "Model" or "StarterCharacterScripts" or "StarterPlayerScripts")
                        ? currentDir
                        : parentDir;

                    ExportHierarchyNodeRecursive(child, destDir, ref scriptCount, ref folderCount, ref modelCount);
                }
            }
        }

        public async Task ExportAsRbxmxAsync(RobloxInstance rootInst, string targetFilePath, Action<string, double>? onProgress = null)
        {
            onProgress?.Invoke("Generando modelo Roblox Studio (.rbxmx) en C# puro...", 0.30);
            await Task.Run(() => ExportAsRbxmx(rootInst, targetFilePath));
            onProgress?.Invoke("¡Modelo Roblox (.rbxmx) exportado con éxito!", 1.0);
        }

        public void DeleteRecursive(int id)
        {
            if (Instances.TryGetValue(id, out var inst))
            {
                var children = inst.ChildrenIds.ToList();
                foreach (int cid in children)
                {
                    DeleteRecursive(cid);
                }
                DeleteInstance(id);
            }
        }

        public void ExportAsRbxmx(RobloxInstance rootInst, string targetFilePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<roblox xmlns:xmime=\"http://www.w3.org/2005/05/xmlmime\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:noNamespaceSchemaLocation=\"http://www.roblox.com/roblox.xsd\" version=\"4\">");
            sb.AppendLine("\t<Meta name=\"ExplicitAutoJoints\">true</Meta>");

            // Recolectar todos los IDs dentro del subárbol a exportar
            var subtreeIds = new HashSet<int>();
            void CollectSubtree(RobloxInstance item)
            {
                subtreeIds.Add(item.Id);
                foreach (var cid in item.ChildrenIds)
                {
                    if (Instances.TryGetValue(cid, out var child))
                        CollectSubtree(child);
                }
            }
            CollectSubtree(rootInst);

            // Preservar clase original (Folder -> Folder, Model -> Model)
            string rootClass = rootInst.IsService ? "Folder" : rootInst.ClassName;
            string rootName = SanitizeForXml(rootInst.Name);

            sb.AppendLine($"\t<Item class=\"{rootClass}\" referent=\"RBX0\">");
            sb.AppendLine("\t\t<Properties>");
            sb.AppendLine($"\t\t\t<string name=\"Name\">{EscapeXml(rootName)}</string>");

            // Emitir propiedades ricas de la raíz si las tiene
            foreach (var kvp in rootInst.XmlProperties)
            {
                if (kvp.Key.Equals("Name", StringComparison.OrdinalIgnoreCase)) continue;
                EmitXmlProperty(sb, kvp.Key, kvp.Value, "\t\t\t", subtreeIds);
            }

            if (rootClass == "Model" && !rootInst.XmlProperties.ContainsKey("PrimaryPart"))
            {
                sb.AppendLine("\t\t\t<Ref name=\"PrimaryPart\">null</Ref>");
            }
            sb.AppendLine("\t\t</Properties>");

            foreach (int childId in rootInst.ChildrenIds)
            {
                if (Instances.TryGetValue(childId, out var childInst))
                {
                    AppendInstanceXml(childInst, sb, 2, subtreeIds);
                }
            }

            sb.AppendLine("\t</Item>");
            sb.AppendLine("</roblox>");
            File.WriteAllText(targetFilePath, sb.ToString(), new UTF8Encoding(false));
        }

        private void AppendInstanceXml(RobloxInstance inst, StringBuilder sb, int indent, HashSet<int> subtreeIds)
        {
            string tabs = new string('\t', indent);
            
            string className = inst.ClassName switch
            {
                "Workspace" => "Model",
                "StarterGui" => "Folder",
                "ReplicatedStorage" => "Folder",
                "ServerScriptService" => "Folder",
                "ServerStorage" => "Folder",
                "Lighting" => "Folder",
                "SoundService" => "Folder",
                "StarterPlayer" => "Folder",
                "StarterPlayerScripts" => "Folder",
                "StarterCharacterScripts" => "Folder",
                _ => inst.ClassName
            };

            string instName = SanitizeForXml(inst.Name);

            sb.AppendLine($"{tabs}<Item class=\"{EscapeXml(className)}\" referent=\"RBX{inst.Id}\">");
            sb.AppendLine($"{tabs}\t<Properties>");
            sb.AppendLine($"{tabs}\t\t<string name=\"Name\">{EscapeXml(instName)}</string>");

            // 1. Si es script, emitir el código Luau actual (incluso si fue editado por el usuario en la UI)
            if (inst.IsScript)
            {
                string rawSrc = inst.Properties.TryGetValue("Source", out var s) ? s : string.Empty;
                string cleanSrc = SanitizeLuaSourceForXml(rawSrc);
                sb.AppendLine($"{tabs}\t\t<Content name=\"LinkedSource\"><null></null></Content>");
                sb.AppendLine($"{tabs}\t\t<ProtectedString name=\"Source\"><![CDATA[{cleanSrc}]]></ProtectedString>");
                if (inst.ClassName is "Script" or "LocalScript")
                {
                    sb.AppendLine($"{tabs}\t\t<bool name=\"Disabled\">false</bool>");
                }
            }

            // 2. Emitir todas las propiedades ricas decodificadas de los chunks PROP
            // (Escalas SpecialMesh, InitialSize, ScaleFactor, shape, Material, CFrame, Color3uint8, etc.)
            foreach (var kvp in inst.XmlProperties)
            {
                if (kvp.Key.Equals("Name", StringComparison.OrdinalIgnoreCase)) continue;
                if (inst.IsScript && (kvp.Key.Equals("Source", StringComparison.OrdinalIgnoreCase) || kvp.Key.Equals("LinkedSource", StringComparison.OrdinalIgnoreCase))) continue;

                EmitXmlProperty(sb, kvp.Key, kvp.Value, $"{tabs}\t\t", subtreeIds);
            }

            // 3. Fallbacks necesarios para modelos
            if (className == "Model" && !inst.XmlProperties.ContainsKey("PrimaryPart"))
            {
                sb.AppendLine($"{tabs}\t\t<Ref name=\"PrimaryPart\">null</Ref>");
            }

            sb.AppendLine($"{tabs}\t</Properties>");

            foreach (int childId in inst.ChildrenIds)
            {
                if (Instances.TryGetValue(childId, out var childInst))
                {
                    AppendInstanceXml(childInst, sb, indent + 1, subtreeIds);
                }
            }

            sb.AppendLine($"{tabs}</Item>");
        }

        private static void EmitXmlProperty(StringBuilder sb, string propName, string xmlSnippet, string prefix, HashSet<int> subtreeIds)
        {
            // Validar si es una referencia cruzada (Ref) para evitar referencias rotas a objetos fuera del paquete exportado
            if (xmlSnippet.StartsWith("<Ref name=\"", StringComparison.OrdinalIgnoreCase))
            {
                int rbxIdx = xmlSnippet.IndexOf(">RBX", StringComparison.Ordinal);
                if (rbxIdx >= 0)
                {
                    int endIdx = xmlSnippet.IndexOf('<', rbxIdx + 4);
                    if (endIdx > rbxIdx + 4 && int.TryParse(xmlSnippet.Substring(rbxIdx + 4, endIdx - (rbxIdx + 4)), out int targetId))
                    {
                        if (!subtreeIds.Contains(targetId))
                        {
                            sb.AppendLine($"{prefix}<Ref name=\"{propName}\">null</Ref>");
                            return;
                        }
                    }
                }
            }

            sb.AppendLine($"{prefix}{xmlSnippet}");
        }


        private static string SanitizeLuaSourceForXml(string source)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;

            string safeCdata = source.Replace("]]>", "]]>]]<![CDATA[>");

            var sb = new StringBuilder(safeCdata.Length + 64);
            foreach (char c in safeCdata)
            {
                int code = (int)c;
                if ((code >= 0x00 && code <= 0x08) || (code >= 0x0B && code <= 0x0C) || (code >= 0x0E && code <= 0x1F))
                {
                    sb.Append($"\\{code}");
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string SanitizeForXml(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                int code = (int)c;
                if ((code >= 0x00 && code <= 0x08) || (code >= 0x0B && code <= 0x0C) || (code >= 0x0E && code <= 0x1F))
                {
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string EscapeXml(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        public struct DecodedCFrame
        {
            public float X, Y, Z;
            public float R00, R01, R02;
            public float R10, R11, R12;
            public float R20, R21, R22;
        }

        private static DecodedCFrame[] DecodeCFrameArray(byte[] data, int offset, int count)
        {
            var cframes = new DecodedCFrame[count];
            int curr = offset;

            for (int i = 0; i < count; i++)
            {
                byte id = data[curr++];
                if (id == 0)
                {
                    cframes[i].R00 = BitConverter.ToSingle(data, curr); curr += 4;
                    cframes[i].R01 = BitConverter.ToSingle(data, curr); curr += 4;
                    cframes[i].R02 = BitConverter.ToSingle(data, curr); curr += 4;
                    cframes[i].R10 = BitConverter.ToSingle(data, curr); curr += 4;
                    cframes[i].R11 = BitConverter.ToSingle(data, curr); curr += 4;
                    cframes[i].R12 = BitConverter.ToSingle(data, curr); curr += 4;
                    cframes[i].R20 = BitConverter.ToSingle(data, curr); curr += 4;
                    cframes[i].R21 = BitConverter.ToSingle(data, curr); curr += 4;
                    cframes[i].R22 = BitConverter.ToSingle(data, curr); curr += 4;
                }
                else
                {
                    GetRotationMatrix(id, out cframes[i].R00, out cframes[i].R01, out cframes[i].R02,
                                          out cframes[i].R10, out cframes[i].R11, out cframes[i].R12,
                                          out cframes[i].R20, out cframes[i].R21, out cframes[i].R22);
                }
            }

            float[] xs = DecodeFloatArray(data, curr, count);
            float[] ys = DecodeFloatArray(data, curr + count * 4, count);
            float[] zs = DecodeFloatArray(data, curr + count * 8, count);

            for (int i = 0; i < count; i++)
            {
                cframes[i].X = xs[i];
                cframes[i].Y = ys[i];
                cframes[i].Z = zs[i];
            }

            return cframes;
        }

        private static void GetRotationMatrix(byte id,
            out float r00, out float r01, out float r02,
            out float r10, out float r11, out float r12,
            out float r20, out float r21, out float r22)
        {
            switch (id)
            {
                case 0x02: r00=1; r01=0; r02=0; r10=0; r11=1; r12=0; r20=0; r21=0; r22=1; break;
                case 0x03: r00=1; r01=0; r02=0; r10=0; r11=0; r12=-1; r20=0; r21=1; r22=0; break;
                case 0x05: r00=1; r01=0; r02=0; r10=0; r11=-1; r12=0; r20=0; r21=0; r22=-1; break;
                case 0x06: r00=1; r01=0; r02=0; r10=0; r11=0; r12=1; r20=0; r21=-1; r22=0; break;
                case 0x07: r00=0; r01=1; r02=0; r10=1; r11=0; r12=0; r20=0; r21=0; r22=-1; break;
                case 0x09: r00=0; r01=1; r02=0; r10=0; r11=0; r12=1; r20=1; r21=0; r22=0; break;
                case 0x0A: r00=0; r01=1; r02=0; r10=-1; r11=0; r12=0; r20=0; r21=0; r22=1; break;
                case 0x0C: r00=0; r01=1; r02=0; r10=0; r11=0; r12=-1; r20=-1; r21=0; r22=0; break;
                case 0x0D: r00=0; r01=0; r02=1; r10=1; r11=0; r12=0; r20=0; r21=1; r22=0; break;
                case 0x0E: r00=0; r01=0; r02=1; r10=0; r11=1; r12=0; r20=-1; r21=0; r22=0; break;
                case 0x10: r00=0; r01=0; r02=1; r10=-1; r11=0; r12=0; r20=0; r21=-1; r22=0; break;
                case 0x11: r00=0; r01=0; r02=1; r10=0; r11=-1; r12=0; r20=1; r21=0; r22=0; break;
                case 0x14: r00=-1; r01=0; r02=0; r10=0; r11=1; r12=0; r20=0; r21=0; r22=-1; break;
                case 0x15: r00=-1; r01=0; r02=0; r10=0; r11=0; r12=1; r20=0; r21=1; r22=0; break;
                case 0x17: r00=-1; r01=0; r02=0; r10=0; r11=-1; r12=0; r20=0; r21=0; r22=1; break;
                case 0x18: r00=-1; r01=0; r02=0; r10=0; r11=0; r12=-1; r20=0; r21=-1; r22=0; break;
                case 0x19: r00=0; r01=-1; r02=0; r10=1; r11=0; r12=0; r20=0; r21=0; r22=1; break;
                case 0x1B: r00=0; r01=-1; r02=0; r10=0; r11=0; r12=1; r20=-1; r21=0; r22=0; break;
                case 0x1C: r00=0; r01=-1; r02=0; r10=-1; r11=0; r12=0; r20=0; r21=0; r22=-1; break;
                case 0x1E: r00=0; r01=-1; r02=0; r10=0; r11=0; r12=-1; r20=1; r21=0; r22=0; break;
                case 0x1F: r00=0; r01=0; r02=-1; r10=1; r11=0; r12=0; r20=0; r21=-1; r22=0; break;
                case 0x20: r00=0; r01=0; r02=-1; r10=0; r11=1; r12=0; r20=1; r21=0; r22=0; break;
                case 0x22: r00=0; r01=0; r02=-1; r10=-1; r11=0; r12=0; r20=0; r21=1; r22=0; break;
                case 0x23: r00=0; r01=0; r02=-1; r10=0; r11=-1; r12=0; r20=-1; r21=0; r22=0; break;
                default: r00=1; r01=0; r02=0; r10=0; r11=1; r12=0; r20=0; r21=0; r22=1; break;
            }
        }

        private static (float X, float Y, float Z)[] DecodeVector3Array(byte[] data, int offset, int count)
        {
            float[] xs = DecodeFloatArray(data, offset, count);
            float[] ys = DecodeFloatArray(data, offset + count * 4, count);
            float[] zs = DecodeFloatArray(data, offset + count * 8, count);

            var res = new (float X, float Y, float Z)[count];
            for (int i = 0; i < count; i++)
            {
                res[i] = (xs[i], ys[i], zs[i]);
            }
            return res;
        }

        private static float[] DecodeFloatArray(byte[] data, int offset, int count)
        {
            var res = new float[count];
            for (int i = 0; i < count; i++)
            {
                byte b0 = data[offset + 0 * count + i];
                byte b1 = data[offset + 1 * count + i];
                byte b2 = data[offset + 2 * count + i];
                byte b3 = data[offset + 3 * count + i];

                uint robloxVal = ((uint)b0 << 24) | ((uint)b1 << 16) | ((uint)b2 << 8) | (uint)b3;
                uint standard = (robloxVal >> 1) | ((robloxVal & 1) << 31);
                res[i] = BitConverter.UInt32BitsToSingle(standard);
            }
            return res;
        }

        private static uint[] DecodeUintArray(byte[] data, int offset, int count)
        {
            var res = new uint[count];
            for (int i = 0; i < count; i++)
            {
                byte b0 = data[offset + 0 * count + i];
                byte b1 = data[offset + 1 * count + i];
                byte b2 = data[offset + 2 * count + i];
                byte b3 = data[offset + 3 * count + i];
                res[i] = ((uint)b0 << 24) | ((uint)b1 << 16) | ((uint)b2 << 8) | b3;
            }
            return res;
        }

        private static int[] DecodeInt32Array(byte[] data, int offset, int count)
        {
            var res = new int[count];
            for (int i = 0; i < count; i++)
            {
                byte b0 = data[offset + 0 * count + i];
                byte b1 = data[offset + 1 * count + i];
                byte b2 = data[offset + 2 * count + i];
                byte b3 = data[offset + 3 * count + i];
                uint u = ((uint)b0 << 24) | ((uint)b1 << 16) | ((uint)b2 << 8) | b3;
                res[i] = (int)((u >> 1) ^ (-(int)(u & 1)));
            }
            return res;
        }

        private static long[] DecodeInt64Array(byte[] data, int offset, int count)
        {
            var res = new long[count];
            for (int i = 0; i < count; i++)
            {
                byte b0 = data[offset + 0 * count + i];
                byte b1 = data[offset + 1 * count + i];
                byte b2 = data[offset + 2 * count + i];
                byte b3 = data[offset + 3 * count + i];
                byte b4 = data[offset + 4 * count + i];
                byte b5 = data[offset + 5 * count + i];
                byte b6 = data[offset + 6 * count + i];
                byte b7 = data[offset + 7 * count + i];
                ulong u = ((ulong)b0 << 56) | ((ulong)b1 << 48) | ((ulong)b2 << 40) | ((ulong)b3 << 32) |
                          ((ulong)b4 << 24) | ((ulong)b5 << 16) | ((ulong)b6 << 8) | b7;
                res[i] = (long)((u >> 1) ^ (ulong)(-(long)(u & 1)));
            }
            return res;
        }

        private static List<int> DecodeReferentArray(byte[] data, int offset, int count)
        {
            var res = new List<int>(count);
            int prev = 0;
            for (int i = 0; i < count; i++)
            {
                byte b0 = data[offset + 0 * count + i];
                byte b1 = data[offset + 1 * count + i];
                byte b2 = data[offset + 2 * count + i];
                byte b3 = data[offset + 3 * count + i];
                uint u = ((uint)b0 << 24) | ((uint)b1 << 16) | ((uint)b2 << 8) | b3;
                int zigzag = (int)((u >> 1) ^ (-(int)(u & 1)));
                prev += zigzag;
                res.Add(prev);
            }
            return res;
        }
    }
}

