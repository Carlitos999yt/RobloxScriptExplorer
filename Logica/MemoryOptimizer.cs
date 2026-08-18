using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RobloxScriptExplorer.Logica
{
    /// <summary>
    /// Utilidad de optimización de memoria RAM de bajo nivel para aplicaciones Windows Desktop.
    /// Libera páginas de memoria no utilizadas devolviéndolas inmediatamente al sistema operativo.
    /// </summary>
    public static class MemoryOptimizer
    {
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern int EmptyWorkingSet(IntPtr hwProc);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

        /// <summary>
        /// Fuerza una recolección de basura completa y purga el Working Set de Windows,
        /// reduciendo el consumo de RAM visible en el Administrador de Tareas al mínimo absoluto.
        /// </summary>
        public static void TrimMemory()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true, true);

                IntPtr handle = Process.GetCurrentProcess().Handle;
                EmptyWorkingSet(handle);
                SetProcessWorkingSetSize(handle, -1, -1);
            }
            catch
            {
                // Silently ignore if OS permission restriction occurs
            }
        }
    }
}
