using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fsp;
using OpenMount.Core.Cache;
using OpenMount.Core.FileSystem;
using OpenMount.Core.Storage;

namespace OpenMount.Core.Manager
{
    public class MountedDriveContext : IAsyncDisposable
    {
        public DriveConfig Config { get; }
        public IStorageProvider Provider { get; }
        public VirtualFileSystem FileSystem { get; }
        public FileSystemHost Host { get; }
        public DateTime MountedAtUtc { get; }

        public MountedDriveContext(
            DriveConfig config,
            IStorageProvider provider,
            VirtualFileSystem fileSystem,
            FileSystemHost host)
        {
            Config = config;
            Provider = provider;
            FileSystem = fileSystem;
            Host = host;
            MountedAtUtc = DateTime.UtcNow;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (FileSystem.AsyncUploadQueue != null)
                {
                    await FileSystem.AsyncUploadQueue.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch { }

            try
            {
                string? shareHost = Config.MountAsNetworkDrive
                    ? (!string.IsNullOrWhiteSpace(Config.NetworkShareHost) ? Config.NetworkShareHost.Trim() : $"OpenMount-{Environment.UserName}")
                    : null;
                string? safeLabel = Config.MountAsNetworkDrive
                    ? (!string.IsNullOrWhiteSpace(Config.Label) ? Config.Label.Trim().Replace('/', '-').Replace('\\', '-') : "Cloud Drive")
                    : null;
                DriveIconHelper.UnregisterDriveIcon(Config.DriveLetter, shareHost, safeLabel);
                Host.Unmount();
                FileSystem.IsMounted = false;
            }
            catch { }

            try
            {
                Host.Dispose();
            }
            catch { }

            try
            {
                await Provider.DisposeAsync().ConfigureAwait(false);
            }
            catch { }
        }
    }

    public class DriveManager : IAsyncDisposable
    {
        private readonly ConcurrentDictionary<string, MountedDriveContext> _mountedDrives =
            new ConcurrentDictionary<string, MountedDriveContext>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<MountedDriveContext> GetMountedDrives() => (IReadOnlyCollection<MountedDriveContext>)_mountedDrives.Values;

        public bool IsMounted(string driveLetter)
        {
            string clean = NormalizeDriveLetter(driveLetter);
            return _mountedDrives.ContainsKey(clean);
        }

        public MountedDriveContext? GetContext(string driveLetter)
        {
            string clean = NormalizeDriveLetter(driveLetter);
            _mountedDrives.TryGetValue(clean, out var context);
            return context;
        }

        public async Task<StorageVolumeInfo?> GetVolumeInfoAsync(string driveLetter, CancellationToken ct = default)
        {
            var ctx = GetContext(driveLetter);
            if (ctx == null) return null;
            try
            {
                return await ctx.Provider.GetVolumeInfoAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        public async Task<MountedDriveContext> MountAsync(DriveConfig config, IStorageProvider provider, CancellationToken ct = default)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (provider == null) throw new ArgumentNullException(nameof(provider));

            string driveLetter = NormalizeDriveLetter(config.DriveLetter);

            if (_mountedDrives.ContainsKey(driveLetter))
            {
                throw new InvalidOperationException($"Drive {driveLetter} is already mounted by OpenMount.");
            }

            if (DosDeviceHelper.IsDriveInUse(driveLetter))
            {
                throw new IOException($"Drive letter {driveLetter} is already in use by the system or another device.");
            }

            if (!WinFspInstaller.IsInstalled())
            {
                throw new InvalidOperationException("WinFsp kernel driver is not installed. Please install WinFsp to enable virtual filesystem mounting.");
            }

            await provider.InitializeAsync(ct).ConfigureAwait(false);

            var cacheManager = new FileCacheManager(driveLetter.TrimEnd(':'), config.StagingPath);
            var vfs = new VirtualFileSystem(provider, config.StagingPath, cacheManager, config);
            var host = new FileSystemHost(vfs);

            string? shareHost = null;
            string? safeLabel = null;

            if (config.MountAsNetworkDrive)
            {
                shareHost = !string.IsNullOrWhiteSpace(config.NetworkShareHost)
                    ? config.NetworkShareHost.Trim()
                    : $"OpenMount-{Environment.UserName}";
                safeLabel = !string.IsNullOrWhiteSpace(config.Label)
                    ? config.Label.Trim().Replace('/', '-').Replace('\\', '-')
                    : "Cloud Drive";

                host.Prefix = $@"\{shareHost}\{safeLabel}";
            }

            int status = host.Mount(driveLetter, null, false, 0);
            if (status != 0)
            {
                host.Dispose();
                await provider.DisposeAsync().ConfigureAwait(false);
                throw new IOException($"Failed to mount drive {driveLetter}. WinFsp status code: 0x{status:X8}");
            }

            vfs.IsMounted = true;

            // Register custom Drive Icon and Label for Windows Explorer
            string? iconPath = DriveIconHelper.ResolveProviderIcon(config.ProviderType, config.CustomIconPath);
            DriveIconHelper.RegisterDriveIcon(
                driveLetter, 
                iconPath, 
                config.Label, 
                config.MountAsNetworkDrive ? shareHost : null, 
                config.MountAsNetworkDrive ? safeLabel : null);

            var context = new MountedDriveContext(config, provider, vfs, host);
            _mountedDrives[driveLetter] = context;

            DosDeviceHelper.NotifyDriveAdded(driveLetter);

            return context;
        }

        public async Task UnmountAsync(string driveLetter)
        {
            string clean = NormalizeDriveLetter(driveLetter);
            if (_mountedDrives.TryRemove(clean, out var context))
            {
                string? shareHost = context.Config.MountAsNetworkDrive
                    ? (!string.IsNullOrWhiteSpace(context.Config.NetworkShareHost) ? context.Config.NetworkShareHost.Trim() : $"OpenMount-{Environment.UserName}")
                    : null;
                string? safeLabel = context.Config.MountAsNetworkDrive
                    ? (!string.IsNullOrWhiteSpace(context.Config.Label) ? context.Config.Label.Trim().Replace('/', '-').Replace('\\', '-') : "Cloud Drive")
                    : null;
                DriveIconHelper.UnregisterDriveIcon(clean, shareHost, safeLabel);
                await context.DisposeAsync().ConfigureAwait(false);
                DosDeviceHelper.NotifyDriveRemoved(clean);
            }
        }

        public async Task UnmountAllAsync()
        {
            foreach (var key in _mountedDrives.Keys)
            {
                try
                {
                    await UnmountAsync(key).ConfigureAwait(false);
                }
                catch { }
            }
        }

        public static string NormalizeDriveLetter(string drive)
        {
            if (string.IsNullOrWhiteSpace(drive))
                throw new ArgumentException("Drive letter cannot be null or empty", nameof(drive));

            string trimmed = drive.Trim().ToUpperInvariant();
            if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
                return trimmed + ":";

            if (trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
                return trimmed;

            if (trimmed.EndsWith("\\"))
                return trimmed.TrimEnd('\\');

            return trimmed;
        }

        public async ValueTask DisposeAsync()
        {
            await UnmountAllAsync().ConfigureAwait(false);
        }
    }
}
