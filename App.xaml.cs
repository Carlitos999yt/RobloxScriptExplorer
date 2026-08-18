using System;
using System.IO;
using System.Linq;
using System.Windows;
using RobloxScriptExplorer.Interfaz;

namespace RobloxScriptExplorer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (Environment.GetCommandLineArgs().Any(a => a.Equals("--test", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    TestRunner.RunTest();
                }
                catch (Exception ex)
                {
                    File.WriteAllText(@"C:\Users\VERONICA\Downloads\test_results.txt", "CRASH: " + ex.ToString());
                }
                Environment.Exit(0);
                return;
            }

            var mainWin = new MainWindow();
            mainWin.Show();
        }
    }
}
