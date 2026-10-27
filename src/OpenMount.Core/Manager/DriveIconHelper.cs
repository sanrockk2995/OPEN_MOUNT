using System;
using System.IO;
using Microsoft.Win32;

namespace OpenMount.Core.Manager
{
    public static class DriveIconHelper
    {
        private const string ExplorerDriveIconsSubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons";

        public static void RegisterDriveIcon(string driveLetter, string? iconPath, string? label)
        {
            if (string.IsNullOrWhiteSpace(driveLetter)) return;

            string letter = driveLetter.Trim().TrimEnd(':', '\\').ToUpperInvariant();
            if (letter.Length != 1 || !char.IsLetter(letter[0])) return;

            try
            {
                using var baseKey = Registry.CurrentUser.CreateSubKey($@"{ExplorerDriveIconsSubKey}\{letter}", writable: true);
                if (baseKey == null) return;

                if (!string.IsNullOrWhiteSpace(iconPath))
                {
                    string checkPath = iconPath.Contains(',') ? iconPath.Split(',')[0].Trim('\"') : iconPath.Trim('\"');
                    if (File.Exists(checkPath))
                    {
                        using var iconKey = baseKey.CreateSubKey("DefaultIcon", writable: true);
                        iconKey?.SetValue(null, iconPath);
                    }
                }

                if (!string.IsNullOrWhiteSpace(label))
                {
                    using var labelKey = baseKey.CreateSubKey("DefaultLabel", writable: true);
                    labelKey?.SetValue(null, label);
                }
            }
            catch { }
        }

        public static void UnregisterDriveIcon(string driveLetter)
        {
            if (string.IsNullOrWhiteSpace(driveLetter)) return;

            string letter = driveLetter.Trim().TrimEnd(':', '\\').ToUpperInvariant();
            if (letter.Length != 1 || !char.IsLetter(letter[0])) return;

            try
            {
                using var driveIconsKey = Registry.CurrentUser.OpenSubKey(ExplorerDriveIconsSubKey, writable: true);
                driveIconsKey?.DeleteSubKeyTree(letter, throwOnMissingSubKey: false);
            }
            catch { }
        }

        public static string? ResolveProviderIcon(string providerType, string? customIconPath = null)
        {
            if (!string.IsNullOrWhiteSpace(customIconPath) && File.Exists(customIconPath))
            {
                return customIconPath;
            }

            try
            {
                // 1. Check application directory for icons
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string type = providerType.ToLowerInvariant();
                string candidate = Path.Combine(baseDir, "icons", $"{type}.ico");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                // 2. Check app.ico
                string appIco = Path.Combine(baseDir, "app.ico");
                if (File.Exists(appIco))
                {
                    return appIco;
                }

                // 3. Check AppData OpenMount icons cache
                string appDataIcons = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OpenMount",
                    "icons",
                    $"{type}.ico");
                if (File.Exists(appDataIcons))
                {
                    return appDataIcons;
                }

                // 4. Fallback to current process executable icon
                string? processPath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
                {
                    return $"{processPath},0";
                }
            }
            catch { }

            return null;
        }
    }
}
