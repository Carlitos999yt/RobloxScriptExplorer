using System.Collections.Generic;

namespace RobloxScriptExplorer.Logica
{
    /// <summary>
    /// Representa los metadatos y la lista de instancias pertenecientes a una clase en el archivo .rbxl.
    /// </summary>
    public class RobloxClassInfo
    {
        public uint ClassId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsService { get; set; }
        public uint Count { get; set; }
        public List<int> InstanceIds { get; } = new();
    }
}
