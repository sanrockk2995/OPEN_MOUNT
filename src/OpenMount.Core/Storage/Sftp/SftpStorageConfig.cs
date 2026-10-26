using System;

namespace OpenMount.Core.Storage.Sftp
{
    public class SftpStorageConfig
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 22;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? PrivateKeyPath { get; set; }
        public string? PrivateKeyPassphrase { get; set; }
        public string RootPath { get; set; } = "/";
    }
}
