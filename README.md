# 🚀 Roblox Binary Place Explorer & Script Injector (.NET 8 C#)

[![C#](https://img.shields.io/badge/Language-C%23%2012%20%2F%20.NET%208.0-blue.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey.svg)](https://microsoft.com)
[![Format](https://img.shields.io/badge/Roblox%20Format-Binary%20.RBXL%20%2F%20.RBXM-red.svg)](https://roblox.com)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

Librería y herramienta de alto rendimiento en **C# (.NET 8)** para **leer, explorar, editar, inyectar scripts Luau, exportar y guardar archivos binarios de Roblox Studio (`.rbxl` / `.rbxmx`)** con **Preservación Quirúrgica del 100% de Chunks** (mallas 3D, terrenos, físicas, uniones e iluminación intactos).

---

## 🌟 Características Principales

* ⚡ **Preservación Quirúrgica de Chunks:** Mantiene el 100% de los objetos 3D (`Part`, `MeshPart`, `Terrain`, `Weld`, `Lighting`), editando e inyectando scripts sin corromper el mapa.
* 📦 **Inyección y Creación Dinámica de Scripts:** Inserta `Script`, `LocalScript` y `ModuleScript` en cualquier carpeta o servicio (`ReplicatedStorage`, `ServerScriptService`, `Workspace`, `StarterGui`, etc.).
* 🛡️ **Sistema de Backups Rotativo FIFO (Máximo 5):** Crea copias de seguridad automáticas en `%LocalAppData%\RobloxScriptExplorer\Backups\` antes de modificar cualquier archivo.
* 🧊 **Exportación a Modelos Roblox (`.rbxmx`) y Luau (`.luau`):** Exporta scripts, interfaces y modelos directamente a archivos Luau limpios y modelos arrastrables en Roblox Studio.
* 🚀 **Bajo Consumo de Memoria RAM (40 MB):** Optimizado con purga automática del Working Set (`MemoryOptimizer.TrimMemory`).
* 💻 **Arquitectura Desacoplada:** El núcleo lógico (`Logica/`) es 100% independiente de la interfaz gráfica y puede ser importado en herramientas de consola, scripts de automatización o bots.

---

## 📁 Estructura del Código (`Logica/`)

| Módulo | Descripción |
| :--- | :--- |
| **`RbxlPlaceManager.cs`** | Fachada principal de alto nivel (`LoadAsync`, `SaveAsync`, `CreateScript`, `DeleteInstance`, `ExportCompleteProject`). |
| **`RobloxBinaryFormat.cs`** | Capa binaria de bajo nivel (Descompresión LZ4, decodificación/codificación zigzag de deltas, validación de encabezado mágico de 14 bytes `<roblox!\x89\xff\r\n\x1a\n`). |
| **`PropChunkHelper.cs`** | Serialización y escalado exacto de propiedades binarias (`Source`, `Name`, `Capabilities`, `UniqueId`, `Enums`). |
| **`BackupService.cs`** | Motor de copias de seguridad automáticas con límite rotativo de 5 versiones por mapa. |
| **`MemoryOptimizer.cs`** | Módulo de optimización y purga de memoria RAM en tiempo real vía APIs de Windows (`EmptyWorkingSet`). |
| **`RobloxInstance.cs`** | Modelo de datos y diagnóstico para instancias de Roblox. |
| **`RobloxClassInfo.cs`** | Metadatos y definición de clases del motor de Roblox Studio. |

---

## 💻 Ejemplos de Uso en C# (Para Desarrolladores e IAs)

### Ejemplo 1: Cargar un mapa e Inyectar 2 Scripts en 2 Carpetas Diferentes

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using RobloxScriptExplorer.Logica;

class Program
{
    static async Task Main()
    {
        // 1. Inicializar el motor y cargar el archivo .rbxl
        var manager = new RbxlPlaceManager();
        await manager.LoadAsync(@"C:\RobloxPlaces\MiJuego.rbxl", (status, progress) => {
            Console.WriteLine($"[{progress * 100:F0}%] {status}");
        });

        // 2. Inyectar Script en ReplicatedStorage
        var repStorage = manager.Instances.Values
            .FirstOrDefault(i => i.Name == "ReplicatedStorage" && i.IsService);

        if (repStorage != null)
        {
            var moduleScript = manager.CreateScript("SharedConfig", "ModuleScript", repStorage.Id);
            moduleScript.Properties["Source"] = "local Config = { Version = '1.0.0' }\nreturn Config\n";
            Console.WriteLine($"✅ Módulo inyectado en ReplicatedStorage (ID: {moduleScript.Id})");
        }

        // 3. Inyectar Script en ServerScriptService
        var serverScripts = manager.Instances.Values
            .FirstOrDefault(i => i.Name == "ServerScriptService" && i.IsService);

        if (serverScripts != null)
        {
            var serverScript = manager.CreateScript("AntiCheatService", "Script", serverScripts.Id);
            serverScript.Properties["Source"] = "print('🛡️ Servicio de Seguridad Iniciado')\n";
            Console.WriteLine($"✅ Script de Servidor inyectado en ServerScriptService (ID: {serverScript.Id})");
        }

        // 4. Guardar y Reemplazar con Auto-Verificación
        string backupCreado = await manager.SaveAsync(@"C:\RobloxPlaces\MiJuego.rbxl");
        Console.WriteLine($"🎉 Guardado con éxito. Backup seguro en: {backupCreado}");
    }
}
```

---

### Ejemplo 2: Modificar el Código de un Script Existente

```csharp
var manager = new RbxlPlaceManager();
await manager.LoadAsync(@"C:\RobloxPlaces\MiJuego.rbxl");

// Buscar el script por su nombre
var script = manager.Instances.Values
    .FirstOrDefault(i => i.Name == "GameManager" && i.IsScript);

if (script != null)
{
    script.Properties["Source"] = "-- Código Luau actualizado automáticamente\nprint('Actualizado!')\n";
    await manager.SaveAsync();
}
```

---

### Ejemplo 3: Exportar todo el Proyecto a Código Fuente Luau (.luau)

```csharp
var manager = new RbxlPlaceManager();
await manager.LoadAsync(@"C:\RobloxPlaces\MiJuego.rbxl");

// Genera una estructura completa de carpetas con archivos .luau, modelos .rbxmx y un manifest JSON
string carpetaDestino = manager.ExportCompleteProject(@"C:\RobloxProyectos\Exportados");
Console.WriteLine($"Proyecto exportado en: {carpetaDestino}");
```

---

## 🏷️ Temas y Palabras Clave para GitHub / Tags
`roblox`, `rbxl`, `rbxm`, `roblox-studio`, `roblox-binary-format`, `luau`, `csharp`, `dotnet8`, `script-injection`, `roblox-parser`, `place-file-editor`, `roblox-tools`

---

## 📄 Licencia
Este proyecto está bajo la Licencia **MIT** - eres libre de usarlo, modificarlo e integrarlo en tus propios proyectos personales o comerciales.
