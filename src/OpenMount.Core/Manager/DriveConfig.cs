using System;
using System.Collections.Generic;

namespace OpenMount.Core.Manager
{
    public class DriveConfig
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string DriveLetter { get; set; } = "Z:";
        public string Label { get; set; } = "OpenMount";
        public string ProviderType { get; set; } = "local"; // "local" or "s3"
        public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public bool ReadOnly { get; set; } = false;
        public bool AutoMount { get; set; } = false;
        public string? StagingPath { get; set; }

        public string? GetSetting(string key)
        {
            return Settings.TryGetValue(key, out var val) ? val : null;
        }

        public void SetSetting(string key, string value)
        {
            Settings[key] = value;
        }
    }
}
