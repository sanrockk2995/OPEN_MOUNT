using System;

namespace OpenMount.Core.Storage.PCloud
{
    public class PCloudStorageConfig
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? AuthToken { get; set; }
        public string Location { get; set; } = "US"; // "US" (api.pcloud.com) or "EU" (eapi.pcloud.com)
        public string RootPath { get; set; } = "/";
    }
}
