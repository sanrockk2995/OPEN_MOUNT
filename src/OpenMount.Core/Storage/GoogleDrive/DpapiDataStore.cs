using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Google.Apis.Json;
using Google.Apis.Util.Store;

namespace OpenMount.Core.Storage.GoogleDrive
{
    public class DpapiDataStore : IDataStore
    {
        private readonly string _storageFolder;

        public DpapiDataStore(string? folderPath = null)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                _storageFolder = Path.Combine(appData, "OpenMount", "tokens");
            }
            else
            {
                _storageFolder = folderPath;
            }

            if (!Directory.Exists(_storageFolder))
            {
                Directory.CreateDirectory(_storageFolder);
            }
        }

        private string GetKeyFilePath(string key)
        {
            string safeKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(key))
                .Replace('/', '_')
                .Replace('+', '-');
            return Path.Combine(_storageFolder, $"{safeKey}.tok.enc");
        }

        public Task StoreAsync<T>(string key, T value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));

            string json = NewtonsoftJsonSerializer.Instance.Serialize(value);
            byte[] plainBytes = Encoding.UTF8.GetBytes(json);
            byte[] encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

            string filePath = GetKeyFilePath(key);
            string tempFile = filePath + ".tmp";
            File.WriteAllBytes(tempFile, encrypted);
            if (File.Exists(filePath)) File.Delete(filePath);
            File.Move(tempFile, filePath);

            return Task.CompletedTask;
        }

        public Task DeleteAsync<T>(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));

            string filePath = GetKeyFilePath(key);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            return Task.CompletedTask;
        }

        public Task<T> GetAsync<T>(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));

            string filePath = GetKeyFilePath(key);
            if (!File.Exists(filePath))
            {
                return Task.FromResult(default(T)!);
            }

            try
            {
                byte[] encrypted = File.ReadAllBytes(filePath);
                byte[] plainBytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(plainBytes);
                T result = NewtonsoftJsonSerializer.Instance.Deserialize<T>(json);
                return Task.FromResult(result);
            }
            catch
            {
                return Task.FromResult(default(T)!);
            }
        }

        public Task ClearAsync()
        {
            if (Directory.Exists(_storageFolder))
            {
                foreach (var file in Directory.EnumerateFiles(_storageFolder, "*.tok.enc"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
            return Task.CompletedTask;
        }
    }
}
