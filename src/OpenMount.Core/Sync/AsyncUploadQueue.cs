using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using OpenMount.Core.FileSystem;
using OpenMount.Core.Storage;

namespace OpenMount.Core.Sync
{
    public class UploadTask
    {
        public string VirtualPath { get; set; } = string.Empty;
        public string LocalFilePath { get; set; } = string.Empty;
        public DateTime QueuedAtUtc { get; set; } = DateTime.UtcNow;
        public int RetryCount { get; set; }
    }

    public class AsyncUploadQueue : IAsyncDisposable
    {
        private readonly IStorageProvider _storage;
        private readonly Action<string>? _onUploadCompleted;
        private readonly Channel<UploadTask> _channel;
        private readonly ConcurrentDictionary<string, UploadTask> _inFlight =
            new ConcurrentDictionary<string, UploadTask>(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Task[] _workers;
        private int _activeUploadCount;

        public int QueueCount => _channel.Reader.Count;
        public int ActiveUploadCount => _activeUploadCount;

        public AsyncUploadQueue(IStorageProvider storage, Action<string>? onUploadCompleted = null, int maxConcurrentWorkers = 2)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _onUploadCompleted = onUploadCompleted;

            _channel = Channel.CreateUnbounded<UploadTask>(new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = false
            });

            _workers = new Task[maxConcurrentWorkers];
            for (int i = 0; i < maxConcurrentWorkers; i++)
            {
                _workers[i] = Task.Run(() => WorkerLoopAsync(_cts.Token));
            }
        }

        public bool Enqueue(string virtualPath, string localFilePath)
        {
            if (string.IsNullOrEmpty(virtualPath) || string.IsNullOrEmpty(localFilePath))
                return false;

            var task = new UploadTask
            {
                VirtualPath = virtualPath,
                LocalFilePath = localFilePath,
                QueuedAtUtc = DateTime.UtcNow
            };

            _inFlight[virtualPath] = task;
            return _channel.Writer.TryWrite(task);
        }

        public bool TryGetInFlightLocalPath(string virtualPath, out string? localPath)
        {
            if (_inFlight.TryGetValue(virtualPath, out var task) && File.Exists(task.LocalFilePath))
            {
                localPath = task.LocalFilePath;
                return true;
            }
            localPath = null;
            return false;
        }

        private async Task WorkerLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                UploadTask task;
                try
                {
                    task = await _channel.Reader.ReadAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                Interlocked.Increment(ref _activeUploadCount);
                try
                {
                    await ProcessUploadWithRetryAsync(task, ct).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref _activeUploadCount);
                    _inFlight.TryRemove(task.VirtualPath, out _);
                }
            }
        }

        private async Task ProcessUploadWithRetryAsync(UploadTask task, CancellationToken ct)
        {
            if (!File.Exists(task.LocalFilePath))
                return;

            const int maxRetries = 3;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using (var fs = new FileStream(task.LocalFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        await _storage.CreateFileAsync(task.VirtualPath, fs, ct).ConfigureAwait(false);
                    }

                    _onUploadCompleted?.Invoke(task.VirtualPath);
                    try { File.Delete(task.LocalFilePath); } catch { }
                    return;
                }
                catch (Exception) when (task.RetryCount < maxRetries && !ct.IsCancellationRequested)
                {
                    task.RetryCount++;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, task.RetryCount)), ct).ConfigureAwait(false);
                }
                catch
                {
                    // If all retries fail, preserve file in unsaved recovery folder
                    try
                    {
                        string recoveryDir = VirtualFileDesc.UnsavedRecoveryDir;
                        if (!Directory.Exists(recoveryDir))
                        {
                            Directory.CreateDirectory(recoveryDir);
                        }
                        string safeName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Path.GetFileName(task.VirtualPath)}";
                        string recoveryPath = Path.Combine(recoveryDir, safeName);
                        File.Copy(task.LocalFilePath, recoveryPath, overwrite: true);
                    }
                    catch { }
                    break;
                }
            }
        }

        public async Task WaitForIdleAsync(TimeSpan timeout)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            while (_channel.Reader.Count > 0 || _activeUploadCount > 0 || !_inFlight.IsEmpty)
            {
                if (timeoutCts.IsCancellationRequested) break;
                await Task.Delay(50, timeoutCts.Token).ConfigureAwait(false);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _channel.Writer.TryComplete();
            _cts.Cancel();
            try
            {
                await Task.WhenAll(_workers).ConfigureAwait(false);
            }
            catch { }
            _cts.Dispose();
        }
    }
}
