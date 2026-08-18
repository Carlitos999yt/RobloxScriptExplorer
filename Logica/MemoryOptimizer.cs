using System;

namespace RobloxScriptExplorer.Logica
{
    /// <summary>
    /// Utilidad de optimización y gestión de memoria RAM mediante recolección administrada .NET.
    /// </summary>
    public static class MemoryOptimizer
    {
        /// <summary>
        /// Ejecuta una recolección de memoria administrada para liberar búferes y memoria no utilizada.
        /// </summary>
        public static void TrimMemory()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            }
            catch
            {
            }
        }
    }
}
