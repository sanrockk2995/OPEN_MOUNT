using System;

namespace OpenMount.Core.Storage.WebDav
{
    public class WebDavStorageConfig
    {
        public string ServerUrl { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string RootPath { get; set; } = "/";
    }
}
