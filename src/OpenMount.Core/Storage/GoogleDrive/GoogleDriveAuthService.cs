using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

namespace OpenMount.Core.Storage.GoogleDrive
{
    public class GoogleDriveAuthService
    {
        private readonly GoogleDriveOAuthConfig _config;
        private readonly DpapiDataStore _dataStore;

        public GoogleDriveAuthService(GoogleDriveOAuthConfig config, DpapiDataStore? dataStore = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _dataStore = dataStore ?? new DpapiDataStore();
        }

        public async Task<UserCredential> AuthorizeAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_config.ClientId) || string.IsNullOrWhiteSpace(_config.ClientSecret))
            {
                throw new InvalidOperationException(
                    "Google Drive OAuth ClientId and ClientSecret must be provided in configuration.");
            }

            var clientSecrets = new ClientSecrets
            {
                ClientId = _config.ClientId,
                ClientSecret = _config.ClientSecret
            };

            // Uses loopback web server receiver on localhost with PKCE
            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                clientSecrets,
                _config.Scopes,
                _config.UserIdentifier,
                ct,
                _dataStore,
                new LocalServerCodeReceiver()).ConfigureAwait(false);

            if (credential.Token.IsStale)
            {
                bool refreshed = await credential.RefreshTokenAsync(ct).ConfigureAwait(false);
                if (!refreshed)
                {
                    throw new InvalidOperationException("Failed to refresh Google Drive OAuth token. Re-authorization required.");
                }
            }

            return credential;
        }

        public async Task<DriveService> CreateDriveServiceAsync(CancellationToken ct = default)
        {
            var credential = await AuthorizeAsync(ct).ConfigureAwait(false);
            return new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "OpenMount"
            });
        }
    }
}
