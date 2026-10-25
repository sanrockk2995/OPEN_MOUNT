using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenMount.Core.Manager;

namespace OpenMount.Core.Security
{
    public class DpapiConfigStore
    {
        private readonly string _defaultConfigPath;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public DpapiConfigStore(string? customConfigPath = null)
        {
            if (!string.IsNullOrEmpty(customConfigPath))
            {
                _defaultConfigPath = customConfigPath;
            }
            else
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string openMountDir = Path.Combine(appData, "OpenMount");
                if (!Directory.Exists(openMountDir))
                {
                    Directory.CreateDirectory(openMountDir);
                }
                _defaultConfigPath = Path.Combine(openMountDir, "drives.dat");
            }
        }

        public async Task SaveDrivesAsync(IEnumerable<DriveConfig> drives, string? path = null, CancellationToken ct = default)
        {
            string targetPath = path ?? _defaultConfigPath;
            string? dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(drives, JsonOptions);
            byte[] plainBytes = Encoding.UTF8.GetBytes(json);
            byte[] encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

            string tempFile = targetPath + ".tmp";
            await File.WriteAllBytesAsync(tempFile, encrypted, ct).ConfigureAwait(false);
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Move(tempFile, targetPath);
        }

        public async Task<List<DriveConfig>> LoadDrivesAsync(string? path = null, CancellationToken ct = default)
        {
            string targetPath = path ?? _defaultConfigPath;
            if (!File.Exists(targetPath))
            {
                return new List<DriveConfig>();
            }

            byte[] encrypted = await File.ReadAllBytesAsync(targetPath, ct).ConfigureAwait(false);
            byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            string json = Encoding.UTF8.GetString(decrypted);

            var result = JsonSerializer.Deserialize<List<DriveConfig>>(json, JsonOptions);
            return result ?? new List<DriveConfig>();
        }
    }
}
