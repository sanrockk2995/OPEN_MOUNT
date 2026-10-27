using System;
using System.IO;
using Microsoft.Win32;

namespace OpenMount.Core.Manager
{
    public static class DriveIconHelper
    {
        private const string ExplorerDriveIconsSubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons";
        private const string ExplorerAppsDrivesSubKey = @"Software\Classes\Applications\Explorer.exe\Drives";
        private const string MountPoints2SubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2";

        public static void RegisterDriveIcon(string driveLetter, string? iconPath, string? label, string? networkShareHost = null, string? shareName = null)
        {
            if (string.IsNullOrWhiteSpace(driveLetter)) return;

            string letter = driveLetter.Trim().TrimEnd(':', '\\').ToUpperInvariant();
            if (letter.Length != 1 || !char.IsLetter(letter[0])) return;

            try
            {
                // 1. Standard DriveIcons (used by local disks and Windows Explorer default)
                using (var baseKey = Registry.CurrentUser.CreateSubKey($@"{ExplorerDriveIconsSubKey}\{letter}", writable: true))
                {
                    if (baseKey != null)
                    {
                        ApplyIconAndLabel(baseKey, iconPath, label);
                    }
                }

                // 2. Classes\Applications\Explorer.exe\Drives (used by Windows Explorer for mapped network drives / DRIVE_REMOTE)
                using (var appDriveKey = Registry.CurrentUser.CreateSubKey($@"{ExplorerAppsDrivesSubKey}\{letter}", writable: true))
                {
                    if (appDriveKey != null)
                    {
                        ApplyIconAndLabel(appDriveKey, iconPath, label);
                    }
                }

                // 3. MountPoints2 (RaiDrive architecture: _LabelFromReg suppresses \\Server-User in Explorer)
                if (!string.IsNullOrWhiteSpace(networkShareHost) && !string.IsNullOrWhiteSpace(shareName))
                {
                    string safeHost = networkShareHost.Trim().Trim('\\');
                    string safeShare = shareName.Trim().Trim('\\');
                    string mountPointKey = $@"{MountPoints2SubKey}\##{safeHost}#{safeShare}";

                    using var mpKey = Registry.CurrentUser.CreateSubKey(mountPointKey, writable: true);
                    if (mpKey != null)
                    {
                        if (!string.IsNullOrWhiteSpace(label))
                        {
                            mpKey.SetValue("_LabelFromReg", label, RegistryValueKind.String);
                        }
                        mpKey.SetValue("_CommentFromDesktopINI", "Connected by OpenMount", RegistryValueKind.String);
                    }
                }

                // 4. Notify Windows Shell to refresh drive icons & labels immediately
                DosDeviceHelper.NotifyShellRefresh();
            }
            catch { }
        }

        private static void ApplyIconAndLabel(RegistryKey key, string? iconPath, string? label)
        {
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                string checkPath = iconPath.Contains(',') ? iconPath.Split(',')[0].Trim('\"') : iconPath.Trim('\"');
                if (File.Exists(checkPath))
                {
                    using var iconKey = key.CreateSubKey("DefaultIcon", writable: true);
                    iconKey?.SetValue(null, iconPath);
                }
            }

            if (!string.IsNullOrWhiteSpace(label))
            {
                using var labelKey = key.CreateSubKey("DefaultLabel", writable: true);
                labelKey?.SetValue(null, label);
            }
        }

        public static void UnregisterDriveIcon(string driveLetter, string? networkShareHost = null, string? shareName = null)
        {
            if (string.IsNullOrWhiteSpace(driveLetter)) return;

            string letter = driveLetter.Trim().TrimEnd(':', '\\').ToUpperInvariant();
            if (letter.Length != 1 || !char.IsLetter(letter[0])) return;

            try
            {
                using (var driveIconsKey = Registry.CurrentUser.OpenSubKey(ExplorerDriveIconsSubKey, writable: true))
                {
                    driveIconsKey?.DeleteSubKeyTree(letter, throwOnMissingSubKey: false);
                }

                using (var appDrivesKey = Registry.CurrentUser.OpenSubKey(ExplorerAppsDrivesSubKey, writable: true))
                {
                    appDrivesKey?.DeleteSubKeyTree(letter, throwOnMissingSubKey: false);
                }

                if (!string.IsNullOrWhiteSpace(networkShareHost) && !string.IsNullOrWhiteSpace(shareName))
                {
                    string safeHost = networkShareHost.Trim().Trim('\\');
                    string safeShare = shareName.Trim().Trim('\\');
                    string mountPointKey = $@"##{safeHost}#{safeShare}";

                    using var mpBaseKey = Registry.CurrentUser.OpenSubKey(MountPoints2SubKey, writable: true);
                    mpBaseKey?.DeleteSubKeyTree(mountPointKey, throwOnMissingSubKey: false);
                }

                DosDeviceHelper.NotifyShellRefresh();
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
