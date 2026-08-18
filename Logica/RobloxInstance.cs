using System;
using System.Collections.Generic;

namespace RobloxScriptExplorer.Logica
{
    /// <summary>
    /// Representa un elemento de propiedad inspeccionable para la interfaz de usuario.
    /// </summary>
    public class RobloxPropertyItem
    {
        public string Category { get; set; } = "General";
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Type { get; set; } = "string";
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// Representa una instancia del motor de Roblox (Script, LocalScript, ModuleScript, Folder, ScreenGui, Servicio, etc.).
    /// </summary>
    public class RobloxInstance
    {
        public int Id { get; set; }
        public uint ClassId { get; set; }
        public string ClassName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int? ParentId { get; set; }
        public bool IsService { get; set; }
        public List<int> ChildrenIds { get; } = new();
        public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IsScript => ClassName is "Script" or "LocalScript" or "ModuleScript";
        public bool IsGui => ClassName is "ScreenGui" or "Frame" or "TextLabel" or "TextButton" or "ImageLabel" or "ImageButton" or "TextBox" or "UIAspectRatioConstraint" or "UICorner" or "UIGradient" or "UIPadding";
        public bool IsRemote => ClassName is "RemoteFunction" or "RemoteEvent" or "BindableEvent" or "BindableFunction";

        public string DisplayIcon => GetVisuals().Icon;

        public (string Icon, string IconColor, string BadgeBg) GetVisuals()
        {
            // Giant Root Services
            if (Name.Equals("ServerScriptService", StringComparison.OrdinalIgnoreCase))
                return ("📂", "#10B981", "#064E3B"); // Emerald Green
            if (Name.Equals("ServerStorage", StringComparison.OrdinalIgnoreCase))
                return ("📦", "#10B981", "#064E3B"); // Emerald Green
            if (Name.Equals("ReplicatedStorage", StringComparison.OrdinalIgnoreCase))
                return ("📦", "#F59E0B", "#78350F"); // Amber / Orange
            if (Name.Equals("ReplicatedFirst", StringComparison.OrdinalIgnoreCase))
                return ("⚡", "#A855F7", "#581C87"); // Purple
            if (Name.Equals("StarterGui", StringComparison.OrdinalIgnoreCase))
                return ("🖥️", "#EAB308", "#713F12"); // Gold / Yellow
            if (Name.Equals("StarterPlayer", StringComparison.OrdinalIgnoreCase))
                return ("👤", "#3B82F6", "#1E3A8A"); // Blue
            if (Name.Equals("StarterPlayerScripts", StringComparison.OrdinalIgnoreCase) || Name.Equals("StarterCharacterScripts", StringComparison.OrdinalIgnoreCase))
                return ("📜", "#38BDF8", "#0C4A6E"); // Cyan
            if (Name.Equals("Workspace", StringComparison.OrdinalIgnoreCase))
                return ("🌐", "#38BDF8", "#0C4A6E"); // Cyan Globe
            if (Name.Equals("Lighting", StringComparison.OrdinalIgnoreCase))
                return ("☀️", "#FACC15", "#713F12"); // Sun Yellow
            if (Name.Equals("SoundService", StringComparison.OrdinalIgnoreCase))
                return ("🔊", "#EC4899", "#831843"); // Pink

            // Scripts, Folders, GUI and Remotes
            return ClassName switch
            {
                "LocalScript" => ("📜", "#38BDF8", "#0C4A6E"),      // Blue / Cyan Client Script
                "Script" => ("📄", "#4ADE80", "#14532D"),           // Green Server Script
                "ModuleScript" => ("📦", "#FB923C", "#7C2D12"),     // Orange Module Script
                "ScreenGui" => ("🖥️", "#60A5FA", "#1E3A8A"),        // Blue ScreenGui
                "Folder" => ("📁", "#FBBF24", "#713F12"),           // Yellow Subfolder
                "RemoteFunction" => ("⚡", "#F43F5E", "#881337"),   // Rose Pink RemoteFunction
                "RemoteEvent" => ("⚡", "#F43F5E", "#881337"),      // Rose Pink RemoteEvent
                "BindableFunction" or "BindableEvent" => ("⚡", "#FB7185", "#881337"),
                "Frame" => ("🔲", "#A78BFA", "#4C1D95"),
                "TextLabel" => ("🔤", "#818CF8", "#312E81"),
                "TextButton" => ("🔘", "#38BDF8", "#0C4A6E"),
                "ImageLabel" or "ImageButton" => ("🖼️", "#C084FC", "#581C87"),
                "Terrain" => ("⛰️", "#34D399", "#064E3B"),
                "SpawnLocation" => ("🚩", "#F87171", "#7F1D1D"),
                _ when IsService => ("📂", "#94A3B8", "#1E293B"),
                _ => ("🔹", "#64748B", "#0F172A")
            };
        }

        public string GetDiagnosticExplanation(string parentName = "Ninguno")
        {
            if (IsScript)
            {
                return string.Empty;
            }

            if (ClassName is "RemoteFunction" or "RemoteEvent" or "BindableFunction" or "BindableEvent")
            {
                return $"=========================================================================================\n" +
                       $"⚡ OBJETO DE RED ROBLOX: {Name} (Clase: {ClassName})\n" +
                       $"   Ubicación: game.{parentName}.{Name} | ID Referent: {Id}\n" +
                       $"=========================================================================================\n\n" +
                       $"❓ ¿POR QUÉ ESTE ARCHIVO NO TIENE CÓDIGO EDITABLE?\n" +
                       $"   En el motor de Roblox, los objetos '{ClassName}' NO son scripts ni almacenan código en su interior.\n" +
                       $"   Son 'túneles' o conectores de red (RPC / Sockets) diseñados para comunicar scripts entre sí.\n\n" +
                       $"⚙️ ¿CÓMO FUNCIONA ESTE OBJETO EN ROBLOX?\n" +
                       (ClassName == "RemoteFunction"
                           ? $"   • Funciona con petición y respuesta bidireccional (Request <-> Response):\n" +
                             $"     - El Cliente (LocalScript) llama a: game.{parentName}.{Name}:InvokeServer(argumentos)\n" +
                             $"     - El Servidor (Script) escucha con: game.{parentName}.{Name}.OnServerInvoke = function(player, ...)\n" +
                             $"     - El servidor procesa los datos y RETORNA una respuesta directa al jugador.\n"
                           : $"   • Funciona con eventos asíncronos unidireccionales (Disparar y olvidar):\n" +
                             $"     - El Cliente usa: game.{parentName}.{Name}:FireServer(argumentos)\n" +
                             $"     - El Servidor escucha con: game.{parentName}.{Name}.OnServerEvent:Connect(function(player, ...))\n") +
                       $"\n" +
                       $"📌 CÓMO INSPECCIONARLO:\n" +
                       $"   • Para ver sus atributos técnicos internos, abre la pestaña '⚙️ Propiedades'.\n" +
                       $"   • Para ver el código que lo utiliza, revisa los LocalScripts en StarterGui o Scripts en ServerScriptService.\n";
            }

            if (IsService)
            {
                string purpose = Name switch
                {
                    "ServerScriptService" => "Ejecutar scripts exclusivos del servidor. Su contenido está totalmente protegido y NUNCA se replica a los jugadores clientes (evitando que hackers o explotadores vean la lógica).",
                    "ReplicatedStorage" => "Almacenar objetos, ModuleScripts y RemoteFunctions que deben ser compartidos y visibles tanto por el Servidor como por todos los Clientes conectados.",
                    "StarterGui" => "Almacenar las interfaces gráficas 2D (ScreenGui). Cuando un jugador entra a la partida, Roblox clona automáticamente todo su contenido dentro del 'PlayerGui' del jugador.",
                    "StarterPlayer" => "Almacenar configuraciones del jugador, físicas del personaje y scripts de inicio (StarterPlayerScripts y StarterCharacterScripts).",
                    "Workspace" => "El mundo físico en 3D del juego donde habitan las partes, el terreno, la gravedad, las colisiones y los personajes de los jugadores.",
                    "Lighting" => "Controlar la iluminación global, sombras, atmósfera, ciclos de día/noche y efectos de postprocesado como Bloom, Blur y SunRays.",
                    "SoundService" => "Gestionar el audio ambiental, grupos de sonido (SoundGroups) y la reverberación acústica del mapa.",
                    "ReplicatedFirst" => "Archivos y scripts de carga rápida que se descargan antes de que el jugador ingrese al juego (ideal para pantallas de carga personalizadas).",
                    _ => "Servicio central del motor C++ de Roblox Studio."
                };

                return $"=========================================================================================\n" +
                       $"📂 SERVICIO PRINCIPAL DE ROBLOX: {Name} (Servicio Raíz)\n" +
                       $"   Hijos directos contenidos: {ChildrenIds.Count} elementos | ID: {Id}\n" +
                       $"=========================================================================================\n\n" +
                       $"❓ ¿POR QUÉ ESTE SERVICIO NO TIENE CÓDIGO DIRECTO?\n" +
                       $"   Los servicios raíz de Roblox no son archivos de texto, sino CONTENEDORES FUNDAMENTALES del motor.\n" +
                       $"   No tienen código propio porque su función es alojar y orquestar a otros scripts y objetos.\n\n" +
                       $"⚙️ ¿CÓMO FUNCIONA ESTE SERVICIO EN EL JUEGO?\n" +
                       $"   • Función técnica:\n" +
                       $"     {purpose}\n\n" +
                       $"   • Acceso desde scripts:\n" +
                       $"     game:GetService(\"{Name}\")\n\n" +
                       $"📌 QUÉ PUEDES HACER:\n" +
                       $"   • Haz clic en el botón '➕ Nuevo Script' para añadir un Script, LocalScript o ModuleScript dentro de este servicio.\n" +
                       $"   • Abre la pestaña '⚙️ Propiedades' para ver los atributos del servicio.\n";
            }

            if (ClassName is "Folder" or "Configuration" or "Model")
            {
                return $"=========================================================================================\n" +
                       $"📁 CONTENEDOR / CARPETA: {Name} (Clase: {ClassName})\n" +
                       $"   Ubicación: game.{parentName}.{Name} | Elementos interiores: {ChildrenIds.Count}\n" +
                       $"=========================================================================================\n\n" +
                       $"❓ ¿POR QUÉ ESTA CARPETA NO TIENE CÓDIGO?\n" +
                       $"   En Roblox, las carpetas '{ClassName}' son objetos organizativos que agrupan scripts, modelos o configuraciones.\n" +
                       $"   No guardan código Luau directamente en sí mismas, sino en los scripts hijos que contienen.\n\n" +
                       $"⚙️ ¿CÓMO SE UTILIZA EN ROBLOX?\n" +
                       $"   • Permite organizar módulos y activos para que los scripts los encuentren ordenadamente:\n" +
                       $"     local carpeta = game.{parentName}:WaitForChild(\"{Name}\")\n" +
                       $"     local elementos = carpeta:GetChildren()\n\n" +
                       $"📌 ACCIONES DISPONIBLES:\n" +
                       $"   • Mantén presionado CTRL y haz clic en esta carpeta para seleccionarla con TODOS sus archivos y exportarla.\n" +
                       $"   • Puedes añadir nuevos scripts dentro de esta carpeta con el botón '➕ Nuevo Script'.\n";
            }

            if (IsGui)
            {
                return $"=========================================================================================\n" +
                       $"🖥️ ELEMENTO DE INTERFAZ GRÁFICA (UI): {Name} (Clase: {ClassName})\n" +
                       $"   Ubicación: game.{parentName}.{Name} | Hijos: {ChildrenIds.Count}\n" +
                       $"=========================================================================================\n\n" +
                       $"❓ ¿POR QUÉ ESTE ELEMENTO DE GUI NO TIENE CÓDIGO TEXTUAL?\n" +
                       $"   Los elementos de interfaz gráfica (ScreenGui, Frames, TextButtons, TextLabels) son COMPONENTES VISUALES 2D.\n" +
                       $"   Su diseño se define mediante propiedades gráficas (posición, tamaño UDim2, color, bordes, fuentes y transparencia).\n\n" +
                       $"⚙️ ¿CÓMO FUNCIONA ESTE ELEMENTO?\n" +
                       $"   • Se dibuja en la pantalla del usuario en el espacio 2D.\n" +
                       $"   • Los LocalScripts interactúan con él escuchando eventos como clics o animaciones:\n" +
                       $"     script.Parent.MouseButton1Click:Connect(function()\n" +
                       $"         print(\"El usuario hizo clic en {Name}!\")\n" +
                       $"     end)\n\n" +
                       $"📌 CÓMO VISUALIZARLO:\n" +
                       $"   • Haz clic en la pestaña '🖥️ Vista Previa de GUI' arriba a la derecha para ver la SIMULACIÓN VISUAL en pantalla.\n" +
                       $"   • Abre la pestaña '⚙️ Propiedades' para inspeccionar sus dimensiones y colores.\n";
            }

            // Fallback for general Roblox instances
            return $"=========================================================================================\n" +
                   $"🔹 INSTANCIA DE ROBLOX: {Name} (Clase: {ClassName})\n" +
                   $"   Ubicación: game.{parentName}.{Name} | ID: {Id}\n" +
                   $"=========================================================================================\n\n" +
                   $"❓ ¿POR QUÉ ESTA INSTANCIA NO TIENE CÓDIGO?\n" +
                   $"   La clase '{ClassName}' es un objeto nativo del motor de Roblox.\n" +
                   $"   No contiene una propiedad 'Source' de código Luau, sino propiedades de configuración y comportamiento del motor.\n\n" +
                   $"⚙️ ¿CÓMO INSPECCIONARLO?\n" +
                   $"   • Abre la pestaña '⚙️ Propiedades' para consultar todos sus atributos técnicos.\n" +
                   $"   • Puedes exportarlo como modelo compatible con Roblox Studio pulsando '🧊 Exportar Modelo (.rbxmx)'.\n";
        }

        public List<RobloxPropertyItem> GetPropertiesList(string parentName = "Ninguno")
        {
            var list = new List<RobloxPropertyItem>
            {
                new() { Category = "Identificación", Name = "Name", Value = Name, Type = "string", Description = "Nombre de la instancia en Roblox" },
                new() { Category = "Identificación", Name = "ClassName", Value = ClassName, Type = "string", Description = "Clase oficial del motor Roblox" },
                new() { Category = "Jerarquía", Name = "Parent", Value = parentName, Type = "Instance", Description = "Contenedor o servicio padre" },
                new() { Category = "Jerarquía", Name = "ChildrenCount", Value = ChildrenIds.Count.ToString(), Type = "int", Description = "Cantidad de objetos hijos directos" },
                new() { Category = "Sistema", Name = "ReferentId", Value = Id.ToString(), Type = "int", Description = "ID de referencia interna en el archivo .rbxl" },
                new() { Category = "Sistema", Name = "IsService", Value = IsService ? "true" : "false", Type = "bool", Description = "Indica si es un Servicio raíz del juego" }
            };

            if (IsScript)
            {
                int srcLen = Properties.TryGetValue("Source", out var s) ? (s?.Length ?? 0) : 0;
                int lines = Properties.TryGetValue("Source", out var s2) && !string.IsNullOrEmpty(s2) ? s2.Split('\n').Length : 0;
                list.Add(new() { Category = "Script", Name = "SourceLength", Value = $"{srcLen:N0} caracteres", Type = "int", Description = "Tamaño del código Luau" });
                list.Add(new() { Category = "Script", Name = "LineCount", Value = $"{lines:N0} líneas", Type = "int", Description = "Total de líneas de código" });
                list.Add(new() { Category = "Script", Name = "Disabled", Value = Properties.TryGetValue("Disabled", out var d) ? d : "false", Type = "bool", Description = "Si el script se ejecuta automáticamente" });
            }

            if (IsRemote)
            {
                list.Add(new() { Category = "Red", Name = "NetworkRole", Value = "Client <-> Server Bridge", Type = "Network", Description = "Puente de comunicación remota entre Cliente y Servidor" });
                list.Add(new() { Category = "Red", Name = "ReplicationScope", Value = "Replicated (Cliente & Servidor)", Type = "Network", Description = "Visible desde LocalScripts y Scripts de Servidor" });
            }

            if (IsGui)
            {
                list.Add(new() { Category = "Interfaz Visual", Name = "GuiType", Value = ClassName, Type = "UI", Description = "Elemento de interfaz gráfica 2D de Roblox" });
                list.Add(new() { Category = "Interfaz Visual", Name = "Enabled / Visible", Value = "true", Type = "bool", Description = "Estado de visibilidad en pantalla" });
            }

            foreach (var kv in Properties)
            {
                if (kv.Key is not "Name" and not "Source" and not "Disabled")
                {
                    list.Add(new() { Category = "Propiedades Específicas", Name = kv.Key, Value = kv.Value, Type = "string", Description = "Propiedad guardada en el archivo binario" });
                }
            }

            return list;
        }
    }
}
