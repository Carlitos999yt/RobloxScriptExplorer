using System;
using System.IO;
using System.Linq;
using System.Text;
using RobloxScriptExplorer.Logica;

namespace RobloxScriptExplorer
{
    public static class TestRunner
    {
        public static void RunTest()
        {
            var sb = new StringBuilder();
            string sourceFile = @"C:\Users\VERONICA\Downloads\Moon Games (multiplayer).rbxl";
            string testOutputFile = @"C:\Users\VERONICA\Downloads\Moon_Games_With_TestScript.rbxl";
            string logFile = @"C:\Users\VERONICA\Downloads\test_results.txt";

            sb.AppendLine("==================================================");
            sb.AppendLine("1. CARGANDO ARCHIVO ORIGINAL...");
            var manager = new RbxlPlaceManager();
            manager.LoadAsync(sourceFile).GetAwaiter().GetResult();
            sb.AppendLine($"   Instancias cargadas: {manager.Instances.Count}");
            sb.AppendLine($"   Clases cargadas: {manager.Classes.Count}");

            // 2. Localizar ReplicatedStorage
            var repStorage = manager.Instances.Values.FirstOrDefault(i => i.Name == "ReplicatedStorage" || (i.IsService && i.Name.Contains("ReplicatedStorage")));
            if (repStorage == null)
            {
                sb.AppendLine("❌ ERROR: No se encontró ReplicatedStorage!");
                File.WriteAllText(logFile, sb.ToString());
                return;
            }
            sb.AppendLine($"2. REPLICATEDSTORAGE ENCONTRADO (ID {repStorage.Id})");

            // 3. Crear TestScript con código de impresión
            sb.AppendLine("3. CREANDO 'TestScript' BAJO ReplicatedStorage...");
            var newScript = manager.CreateScript("TestScript", "Script", repStorage.Id);
            newScript.Properties["Source"] = "print(\"¡Hola Mundo desde TestScript en ReplicatedStorage!\")\n";
            sb.AppendLine($"   Script Creado: {newScript.Name} (ID {newScript.Id}, Padre {newScript.ParentId})");

            // 4. Guardar archivo
            sb.AppendLine("4. GUARDANDO Y RECONSTRUYENDO BINARIO EN:");
            sb.AppendLine($"   {testOutputFile}");
            string bak = manager.Save(testOutputFile);
            sb.AppendLine($"   Guardado exitoso. Copia de respaldo: {bak}");

            // 5. Reabrir el archivo guardado y verificar
            sb.AppendLine("\n5. REABRIENDO ARCHIVO GUARDADO PARA VERIFICAR...");
            var verifyManager = new RbxlPlaceManager();
            verifyManager.LoadAsync(testOutputFile).GetAwaiter().GetResult();
            sb.AppendLine($"   Instancias leídas en archivo guardado: {verifyManager.Instances.Count}");

            var found = verifyManager.Instances.Values.FirstOrDefault(i => i.Name == "TestScript");
            if (found != null)
            {
                string parentName = found.ParentId.HasValue && verifyManager.Instances.TryGetValue(found.ParentId.Value, out var p) ? p.Name : "Desconocido";
                string src = found.Properties.TryGetValue("Source", out var s) ? s : "(Vacío)";
                sb.AppendLine("\n==================================================");
                sb.AppendLine("🎉 ¡VERIFICACIÓN EXITOSA AL 100%!");
                sb.AppendLine($"✅ Script: {found.Name} ({found.ClassName})");
                sb.AppendLine($"✅ ID de Instancia: {found.Id}");
                sb.AppendLine($"✅ Contenedor Padre: {parentName} (ID {found.ParentId})");
                sb.AppendLine($"✅ Código Guardado:\n{src.Trim()}");
                sb.AppendLine("==================================================");
            }
            else
            {
                sb.AppendLine("❌ ERROR: No se encontró TestScript en el archivo guardado.");
            }

            File.WriteAllText(logFile, sb.ToString(), Encoding.UTF8);
        }
    }
}
