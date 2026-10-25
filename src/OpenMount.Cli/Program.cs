using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenMount.Core.Manager;
using OpenMount.Core.Security;
using OpenMount.Core.Storage;
using OpenMount.Core.Storage.Local;
using OpenMount.Core.Storage.S3;
using OpenMount.Core.Storage.GoogleDrive;

namespace OpenMount.Cli
{
    class Program
    {
        private static readonly DriveManager Manager = new DriveManager();
        private static readonly DpapiConfigStore ConfigStore = new DpapiConfigStore();
        private static readonly ManualResetEventSlim ShutdownEvent = new ManualResetEventSlim(false);

        static async Task<int> Main(string[] args)
        {
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\n[OpenMount] Shutdown signal received. Unmounting drives...");
                ShutdownEvent.Set();
            };

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                try
                {
                    Manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch { }
            };

            if (args.Length == 0 || args[0].Equals("--help", StringComparison.OrdinalIgnoreCase) || args[0].Equals("-h", StringComparison.OrdinalIgnoreCase))
            {
                PrintHelp();
                return 0;
            }

            string command = args[0].ToLowerInvariant();
            var options = ParseArgs(args.Skip(1).ToArray());

            try
            {
                switch (command)
                {
                    case "mount":
                        return await HandleMountAsync(options);

                    case "config":
                        return await HandleConfigAsync(options);

                    case "list":
                        return await HandleListAsync();

                    case "version":
                        Console.WriteLine("OpenMount v1.0.0 (WinFsp-backed Virtual Cloud Storage)");
                        return 0;

                    default:
                        Console.Error.WriteLine($"[Error] Unknown command '{command}'. Use 'openmount --help' for usage.");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"[Fatal Error] {ex.Message}");
                if (options.ContainsKey("verbose"))
                {
                    Console.Error.WriteLine(ex.ToString());
                }
                Console.ResetColor();
                return 1;
            }
            finally
            {
                await Manager.DisposeAsync();
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine(@"OpenMount - High-Performance Virtual Drive Mounter for Windows
Built with WinFsp and .NET 8.0

Usage:
  openmount <command> [options]

Commands:
  mount         Mount a storage provider to a Windows drive letter
  config        Manage encrypted drive configurations (DPAPI)
  list          List active mounted drives
  version       Display OpenMount version

Mount Options:
  -l, --letter <letter>     Drive letter (e.g. Z: or Z)
  -p, --provider <type>     Provider type: 'local' or 's3' (default: local)
  --label <label>           Drive volume label (default: OpenMount)
  --path <path>             Root directory for local provider
  --bucket <name>           Bucket name for S3 provider
  --endpoint <url>          S3 endpoint URL (e.g. https://s3.amazonaws.com)
  --region <region>         S3 region (default: us-east-1)
  --key <key>               S3 Access Key
  --secret <secret>         S3 Secret Key
  --staging <dir>           Directory for write cache / staging
  --save                    Save configuration to DPAPI encrypted store

Config Commands:
  config list               List all securely stored drive configurations
  config save <options>     Save configuration without immediately mounting

Examples:
  openmount mount -l Z: -p local --path C:\TestStorage --label ""Test Drive""
  openmount mount -l Y: -p s3 --bucket my-s3-bucket --key AKIA... --secret ...
");
        }

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg.StartsWith("--"))
                {
                    string key = arg.Substring(2);
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        dict[key] = args[++i];
                    }
                    else
                    {
                        dict[key] = "true";
                    }
                }
                else if (arg.StartsWith("-"))
                {
                    string key = arg.TrimStart('-');
                    if (key.Equals("l", StringComparison.OrdinalIgnoreCase)) key = "letter";
                    else if (key.Equals("p", StringComparison.OrdinalIgnoreCase)) key = "provider";
                    else if (key.Equals("h", StringComparison.OrdinalIgnoreCase)) key = "help";

                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        dict[key] = args[++i];
                    }
                    else
                    {
                        dict[key] = "true";
                    }
                }
                else if (i == 0)
                {
                    dict["subcommand"] = arg;
                }
            }
            return dict;
        }

        private static async Task<int> HandleMountAsync(Dictionary<string, string> opts)
        {
            if (!opts.TryGetValue("letter", out var letter) || string.IsNullOrWhiteSpace(letter))
            {
                Console.Error.WriteLine("[Error] Drive letter is required (-l or --letter, e.g. Z:)");
                return 1;
            }

            string providerType = opts.TryGetValue("provider", out var p) ? p.ToLowerInvariant() : "local";
            string label = opts.TryGetValue("label", out var lbl) ? lbl : "OpenMount";
            string? staging = opts.TryGetValue("staging", out var stg) ? stg : null;

            var config = new DriveConfig
            {
                DriveLetter = letter,
                Label = label,
                ProviderType = providerType,
                StagingPath = staging
            };

            IStorageProvider provider;

            if (providerType == "local")
            {
                if (!opts.TryGetValue("path", out var localPath) || string.IsNullOrWhiteSpace(localPath))
                {
                    Console.Error.WriteLine("[Error] Local provider requires --path <directory>");
                    return 1;
                }

                config.SetSetting("path", localPath);
                provider = new LocalStorageProvider(localPath, label);
            }
            else if (providerType == "s3")
            {
                if (!opts.TryGetValue("bucket", out var bucket) || string.IsNullOrWhiteSpace(bucket))
                {
                    Console.Error.WriteLine("[Error] S3 provider requires --bucket <name>");
                    return 1;
                }

                string key = opts.TryGetValue("key", out var k) ? k : string.Empty;
                string secret = opts.TryGetValue("secret", out var s) ? s : string.Empty;
                string endpoint = opts.TryGetValue("endpoint", out var ep) ? ep : "https://s3.amazonaws.com";
                string region = opts.TryGetValue("region", out var r) ? r : "us-east-1";

                config.SetSetting("bucket", bucket);
                config.SetSetting("endpoint", endpoint);
                config.SetSetting("region", region);
                config.SetSetting("key", key);
                config.SetSetting("secret", secret);

                var s3Cfg = new S3StorageConfig
                {
                    BucketName = bucket,
                    AccessKey = key,
                    SecretKey = secret,
                    ServiceUrl = endpoint,
                    Region = region
                };

                provider = new S3StorageProvider(s3Cfg, displayName: label);
            }
            else if (providerType == "gdrive" || providerType == "google")
            {
                if (!opts.TryGetValue("client-id", out var clientId) || string.IsNullOrWhiteSpace(clientId))
                {
                    Console.Error.WriteLine("[Error] Google Drive provider requires --client-id <oauth_client_id>");
                    return 1;
                }

                string clientSecret = opts.TryGetValue("client-secret", out var cs) ? cs : string.Empty;
                string user = opts.TryGetValue("user", out var u) ? u : "default";

                config.SetSetting("client_id", clientId);
                config.SetSetting("client_secret", clientSecret);
                config.SetSetting("user", user);

                var gConfig = new GoogleDriveOAuthConfig
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret,
                    UserIdentifier = user
                };

                Console.WriteLine("[OpenMount] Initiating Google OAuth 2.0 PKCE authentication...");
                var authService = new GoogleDriveAuthService(gConfig);
                var driveService = await authService.CreateDriveServiceAsync();

                provider = new GoogleDriveStorageProvider(gConfig, driveService, disposeService: true, displayName: label);
            }
            else
            {
                Console.Error.WriteLine($"[Error] Unsupported provider type '{providerType}'. Supported: local, s3, gdrive");
                return 1;
            }

            if (opts.ContainsKey("save"))
            {
                var existing = await ConfigStore.LoadDrivesAsync();
                existing.RemoveAll(x => string.Equals(x.DriveLetter, config.DriveLetter, StringComparison.OrdinalIgnoreCase));
                existing.Add(config);
                await ConfigStore.SaveDrivesAsync(existing);
                Console.WriteLine($"[Config] Configuration saved securely via DPAPI for drive {config.DriveLetter}");
            }

            Console.WriteLine($"[OpenMount] Mounting {provider.DisplayName} as {DriveManager.NormalizeDriveLetter(config.DriveLetter)}...");
            var mounted = await Manager.MountAsync(config, provider);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Success] Drive {DriveManager.NormalizeDriveLetter(config.DriveLetter)} mounted successfully.");
            Console.WriteLine($"[Info] Press Ctrl+C to unmount and exit.");
            Console.ResetColor();

            ShutdownEvent.Wait();

            Console.WriteLine($"[OpenMount] Unmounting {DriveManager.NormalizeDriveLetter(config.DriveLetter)}...");
            await Manager.UnmountAsync(config.DriveLetter);
            Console.WriteLine($"[Success] Drive unmounted cleanly.");
            return 0;
        }

        private static async Task<int> HandleConfigAsync(Dictionary<string, string> opts)
        {
            string sub = opts.TryGetValue("subcommand", out var s) ? s.ToLowerInvariant() : "list";
            if (sub == "list")
            {
                var drives = await ConfigStore.LoadDrivesAsync();
                if (drives.Count == 0)
                {
                    Console.WriteLine("[Config] No saved drive configurations found.");
                    return 0;
                }

                Console.WriteLine($"{"Letter",-8} {"Provider",-10} {"Label",-20} {"Details"}");
                Console.WriteLine(new string('-', 60));
                foreach (var d in drives)
                {
                    string details = d.ProviderType == "local"
                        ? d.GetSetting("path") ?? ""
                        : $"Bucket: {d.GetSetting("bucket")}";
                    Console.WriteLine($"{d.DriveLetter,-8} {d.ProviderType,-10} {d.Label,-20} {details}");
                }
                return 0;
            }

            Console.Error.WriteLine($"[Error] Unknown config command '{sub}'.");
            return 1;
        }

        private static async Task<int> HandleListAsync()
        {
            var active = Manager.GetMountedDrives();
            if (active.Count == 0)
            {
                Console.WriteLine("[OpenMount] No active drives currently mounted in this process.");
                return 0;
            }

            Console.WriteLine($"{"Letter",-8} {"Provider",-12} {"Capacity (Used / Total)",-28} {"Mounted At",-22} {"Label"}");
            Console.WriteLine(new string('-', 85));
            foreach (var m in active)
            {
                string cap = "N/A";
                try
                {
                    var vol = await m.Provider.GetVolumeInfoAsync();
                    if (vol != null && vol.TotalSize > 0)
                    {
                        ulong used = vol.TotalSize > vol.FreeSize ? vol.TotalSize - vol.FreeSize : 0;
                        cap = $"{FormatBytes(used)} / {FormatBytes(vol.TotalSize)}";
                    }
                }
                catch { }

                Console.WriteLine($"{m.Config.DriveLetter,-8} {m.Provider.ProviderId,-12} {cap,-28} {m.MountedAtUtc.ToLocalTime(),-22} {m.Config.Label}");
            }
            return 0;
        }

        private static string FormatBytes(ulong bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB", "PB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024.0 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024.0;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
