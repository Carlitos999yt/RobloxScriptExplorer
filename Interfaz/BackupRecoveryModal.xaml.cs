using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using RobloxScriptExplorer.Logica;

namespace RobloxScriptExplorer.Interfaz
{
    public partial class BackupRecoveryModal : Window
    {
        public BackupItem? SelectedBackup => GridBackups.SelectedItem as BackupItem;
        public bool RestoreRequested { get; private set; } = false;

        public BackupRecoveryModal(string? currentPlaceName = null)
        {
            InitializeComponent();
            LoadBackups(currentPlaceName);
        }

        private void LoadBackups(string? currentPlaceName)
        {
            var backups = BackupService.GetBackups(currentPlaceName);
            if (backups.Count == 0 && !string.IsNullOrEmpty(currentPlaceName))
            {
                // Fallback to all backups
                backups = BackupService.GetBackups(null);
            }
            GridBackups.ItemsSource = backups;
            if (backups.Count > 0)
            {
                GridBackups.SelectedIndex = 0;
            }
        }

        private void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedBackup == null)
            {
                MessageBox.Show("Por favor selecciona un archivo de respaldo de la lista.", "Selección Requerida", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var res = MessageBox.Show($"¿Deseas restaurar el backup del {SelectedBackup.TimeFormatted} para el mapa '{SelectedBackup.PlaceName}'?\n\nEsto reemplazará el archivo actual con la versión guardada en el backup.", "Confirmar Restauración", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                RestoreRequested = true;
                DialogResult = true;
                Close();
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dir = BackupService.BackupDirectory;
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al abrir la carpeta:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
