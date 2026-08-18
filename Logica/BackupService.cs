using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RobloxScriptExplorer.Logica
{
    public class BackupItem
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string PlaceName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string TimeFormatted => Timestamp.ToString("dd/MM/yyyy HH:mm:ss");
        public string SizeFormatted { get; set; } = string.Empty;
    }

    public static class BackupService
    {
        public static string BackupDirectory
        {
            get
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string backupDir = Path.Combine(localAppData, "RobloxScriptExplorer", "Backups");
                Directory.CreateDirectory(backupDir);
                return backupDir;
            }
        }

        public static string CreateBackup(string sourceFilePath)
        {
            if (!File.Exists(sourceFilePath))
                return string.Empty;

            string placeName = Path.GetFileNameWithoutExtension(sourceFilePath);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupFileName = $"{placeName}_{timestamp}.rbxl";
            string destPath = Path.Combine(BackupDirectory, backupFileName);

            File.Copy(sourceFilePath, destPath, true);

            // Enforce strictly maximum 5 backups per place (rolling FIFO rotation)
            RotateBackups(placeName, 5);

            return destPath;
        }

        public static void RotateBackups(string placeName, int maxBackups = 5)
        {
            try
            {
                var dir = new DirectoryInfo(BackupDirectory);
                var placeBackups = dir.GetFiles($"{placeName}_*.rbxl")
                    .OrderBy(f => f.CreationTime)
                    .ToList();

                while (placeBackups.Count > maxBackups)
                {
                    var oldest = placeBackups[0];
                    oldest.Delete();
                    placeBackups.RemoveAt(0);
                }
            }
            catch
            {
                // Ignore rotation errors
            }
        }

        public static List<BackupItem> GetBackups(string? placeName = null)
        {
            var list = new List<BackupItem>();
            var dir = new DirectoryInfo(BackupDirectory);
            if (!dir.Exists) return list;

            string searchPattern = string.IsNullOrEmpty(placeName) ? "*.rbxl" : $"{placeName}_*.rbxl";
            var files = dir.GetFiles(searchPattern)
                .OrderByDescending(f => f.CreationTime)
                .ToList();

            foreach (var f in files)
            {
                string pName = f.Name;
                int lastUnderscore = f.Name.LastIndexOf('_');
                if (lastUnderscore > 0)
                {
                    pName = f.Name.Substring(0, lastUnderscore);
                }

                double sizeMb = f.Length / (1024.0 * 1024.0);
                string sizeStr = sizeMb >= 1.0 ? $"{sizeMb:F2} MB" : $"{f.Length / 1024.0:F1} KB";

                list.Add(new BackupItem
                {
                    FileName = f.Name,
                    FullPath = f.FullName,
                    PlaceName = pName,
                    Timestamp = f.CreationTime,
                    SizeFormatted = sizeStr
                });
            }

            return list;
        }

        public static void RestoreBackup(string backupFilePath, string destinationFilePath)
        {
            if (!File.Exists(backupFilePath))
                throw new FileNotFoundException("El archivo de backup no existe.", backupFilePath);

            File.Copy(backupFilePath, destinationFilePath, true);
        }
    }
}
