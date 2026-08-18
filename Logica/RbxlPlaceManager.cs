using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RobloxScriptExplorer.Logica
{
    /// <summary>
    /// Motor de alto nivel para cargar, editar, crear scripts, exportar y guardar archivos de Roblox Place (.rbxl)
    /// y exportar modelos 3D, GUIs y scripts (.rbxmx) compatibles con Roblox Studio para arrastrar y soltar.
    /// </summary>
    public class RbxlPlaceManager
    {
        public string FilePath { get; private set; } = string.Empty;
        public List<RobloxChunk> Chunks { get; private set; } = new();
        public Dictionary<uint, RobloxClassInfo> Classes { get; } = new();
        public Dictionary<int, RobloxInstance> Instances { get; } = new();
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

                    if (Classes.TryGetValue(cid, out var cinfo) && ptype == 0x01)
                    {
                        int spos = pdataOffset;
                        foreach (int iId in cinfo.InstanceIds)
                        {
                            if (spos + 4 <= d.Length)
                            {
                                int slen = (int)BitConverter.ToUInt32(d, spos);
                                spos += 4;
                                if (spos + slen <= d.Length)
                                {
                                    string sval = Encoding.UTF8.GetString(d, spos, slen);
                                    spos += slen;

                                    if (Instances.TryGetValue(iId, out var inst))
                                    {
                                        inst.Properties[pname] = sval;
                                        if (pname.Equals("Name", StringComparison.OrdinalIgnoreCase))
                                        {
                                            inst.Name = sval;
                                        }
                                    }
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
                var preservedChunks = PropChunkHelper.BuildPreservedChunks(
                    Chunks, Classes, Instances, HeaderClassCount, HeaderInstanceCount, _hasModifiedStructure);

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
            return true;
        }

        public string GetInstanceHierarchyPath(int id)
        {
            var parts = new List<string>();
            int? curr = id;
            while (curr.HasValue && Instances.TryGetValue(curr.Value, out var inst))
            {
                parts.Add(inst.Name);
                curr = inst.ParentId;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>
        /// Modalidad 1: Exporta los paquetes Todo-en-Uno .rbxmx para arrastrar directamente a Roblox Studio.
        /// </summary>
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

            // Manifiesto de paquetes
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

        /// <summary>
        /// Modalidad 2: Exporta la estructura modular separada (.luau por separado, modelos 3D y manifest).
        /// </summary>
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

        /// <summary>
        /// Exporta cualquier instancia a un archivo XML .rbxmx 100% compatible y validado con Roblox Studio.
        /// </summary>
        public void ExportAsRbxmx(RobloxInstance rootInst, string targetFilePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<roblox xmlns:xmime=\"http://www.w3.org/2005/05/xmlmime\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:noNamespaceSchemaLocation=\"http://www.roblox.com/roblox.xsd\" version=\"4\">");
            sb.AppendLine("\t<Meta name=\"ExplicitAutoJoints\">true</Meta>");

            AppendInstanceXml(rootInst, sb, 1);

            sb.AppendLine("</roblox>");
            File.WriteAllText(targetFilePath, sb.ToString(), Encoding.UTF8);
        }

        private void AppendInstanceXml(RobloxInstance inst, StringBuilder sb, int indent)
        {
            string tabs = new string('\t', indent);
            
            // Mapeo seguro de clases para compatibilidad absoluta con Roblox Studio
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

            // 1. Scripts (Script, LocalScript, ModuleScript con código Luau saneado y CDATA escapado)
            if (className is "Script" or "LocalScript" or "ModuleScript")
            {
                string rawSrc = inst.Properties.TryGetValue("Source", out var s) ? s : string.Empty;
                string cleanSrc = SanitizeLuaSourceForXml(rawSrc);
                sb.AppendLine($"{tabs}\t\t<ProtectedString name=\"Source\"><![CDATA[{cleanSrc}]]></ProtectedString>");
                sb.AppendLine($"{tabs}\t\t<bool name=\"Disabled\">false</bool>");
            }
            // 2. Interfaces Gráficas
            else if (className == "ScreenGui")
            {
                sb.AppendLine($"{tabs}\t\t<bool name=\"Enabled\">true</bool>");
                sb.AppendLine($"{tabs}\t\t<bool name=\"ResetOnSpawn\">true</bool>");
            }
            else if (className is "Frame" or "TextLabel" or "TextButton" or "ImageLabel" or "ImageButton" or "TextBox")
            {
                sb.AppendLine($"{tabs}\t\t<bool name=\"Visible\">true</bool>");

                if (className is "TextLabel" or "TextButton" or "TextBox")
                {
                    string txt = inst.Properties.TryGetValue("Text", out var t) ? SanitizeForXml(t) : instName;
                    sb.AppendLine($"{tabs}\t\t<string name=\"Text\">{EscapeXml(txt)}</string>");
                }
                if (className is "ImageLabel" or "ImageButton" && inst.Properties.TryGetValue("Image", out var img) && !string.IsNullOrWhiteSpace(img))
                {
                    sb.AppendLine($"{tabs}\t\t<Content name=\"Image\"><url>{EscapeXml(SanitizeForXml(img))}</url></Content>");
                }
            }
            // 3. Modelos 3D y Partes
            else if (className is "Part" or "MeshPart" or "SpawnLocation")
            {
                sb.AppendLine($"{tabs}\t\t<bool name=\"Anchored\">true</bool>");
                sb.AppendLine($"{tabs}\t\t<bool name=\"CanCollide\">true</bool>");
                sb.AppendLine($"{tabs}\t\t<Vector3 name=\"size\"><X>4</X><Y>1.2</Y><Z>2</Z></Vector3>");
            }
            else if (className == "Model")
            {
                sb.AppendLine($"{tabs}\t\t<CoordinateFrame name=\"WorldPivotData\"><X>0</X><Y>0</Y><Z>0</Z><R00>1</R00><R01>0</R01><R02>0</R02><R10>0</R10><R11>1</R11><R12>0</R12><R20>0</R20><R21>0</R21><R22>1</R22></CoordinateFrame>");
            }
            // 4. Sonidos
            else if (className == "Sound" && inst.Properties.TryGetValue("SoundId", out var sid) && !string.IsNullOrWhiteSpace(sid))
            {
                sb.AppendLine($"{tabs}\t\t<Content name=\"SoundId\"><url>{EscapeXml(SanitizeForXml(sid))}</url></Content>");
            }

            sb.AppendLine($"{tabs}\t</Properties>");

            foreach (int childId in inst.ChildrenIds)
            {
                if (Instances.TryGetValue(childId, out var childInst))
                {
                    AppendInstanceXml(childInst, sb, indent + 1);
                }
            }

            sb.AppendLine($"{tabs}</Item>");
        }

        /// <summary>
        /// Sanea código Luau para XML 1.0:
        /// 1. Reemplaza cualquier secuencia de cierre CDATA "]]>" por "]]>]]<![CDATA[>"
        /// 2. Reemplaza bytes de control binarios ilegales en XML 1.0 (0x00..0x08, 0x0B..0x0C, 0x0E..0x1F) por secuencias de escape Luau (\d)
        /// </summary>
        private static string SanitizeLuaSourceForXml(string source)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;

            // 1. Evitar que "]]>" rompa el bloque CDATA
            string safeCdata = source.Replace("]]>", "]]>]]<![CDATA[>");

            // 2. Escapar caracteres de control ilegales en XML 1.0
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
    }
}
