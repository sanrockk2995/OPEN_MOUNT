using System;

namespace OpenMount.Core.Storage.GoogleDrive
{
    public class GoogleDriveOAuthConfig
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string UserIdentifier { get; set; } = "default";
        public string[] Scopes { get; set; } = new[]
        {
            Google.Apis.Drive.v3.DriveService.Scope.Drive
        };
        public string RootFolderId { get; set; } = "root";
        public bool ExportWorkspaceDocuments { get; set; } = true;
    }
}
