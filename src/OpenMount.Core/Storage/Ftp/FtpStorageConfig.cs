using System;

namespace OpenMount.Core.Storage.Ftp
{
    public class FtpStorageConfig
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 21;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Encryption { get; set; } = "None"; // None, Explicit, Implicit
        public string RootPath { get; set; } = "/";
    }
}
