using System;
using System.Collections.Generic;

namespace OpenMount.Core.Manager
{
    public enum ConflictResolutionMode
    {
        OverwriteTarget = 0,
        RenameSource = 1,
        Skip = 2
    }

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

        // Advanced Options (RaiDrive Mode)
        public bool EnableFilePreview { get; set; } = true;
        public bool InstantProperties { get; set; } = false;
        public bool AsyncUpload { get; set; } = false;
        public bool MaintainDateModified { get; set; } = true;
        public bool MonitorProcessAccess { get; set; } = false;
        public bool QuickRemoteOperations { get; set; } = true;
        public ConflictResolutionMode ConflictResolution { get; set; } = ConflictResolutionMode.OverwriteTarget;

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
