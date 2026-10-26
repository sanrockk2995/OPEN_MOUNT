using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace OpenMount.Core.Monitoring
{
    public class FileAccessEvent
    {
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
    }

    public class ProcessAccessMonitor
    {
        private readonly ConcurrentDictionary<int, string> _processNameCache = new ConcurrentDictionary<int, string>();
        private readonly ConcurrentQueue<FileAccessEvent> _events = new ConcurrentQueue<FileAccessEvent>();
        private const int MaxEvents = 200;

        public bool IsEnabled { get; set; }

        public ProcessAccessMonitor(bool isEnabled = false)
        {
            IsEnabled = isEnabled;
        }

        public void RecordAccess(int processId, string filePath, string operation)
        {
            if (!IsEnabled || processId <= 0) return;

            string procName = GetProcessName(processId);
            var evt = new FileAccessEvent
            {
                TimestampUtc = DateTime.UtcNow,
                ProcessId = processId,
                ProcessName = procName,
                FilePath = filePath,
                Operation = operation
            };

            _events.Enqueue(evt);
            while (_events.Count > MaxEvents && _events.TryDequeue(out _)) { }
        }

        public string GetProcessName(int processId)
        {
            if (processId <= 4) return "System";

            return _processNameCache.GetOrAdd(processId, pid =>
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    return p.ProcessName;
                }
                catch
                {
                    return $"PID:{pid}";
                }
            });
        }

        public bool IsPreviewProcess(int processId)
        {
            string name = GetProcessName(processId);
            return name.Equals("prevhost", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("thumbnailhost", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("SearchProtocolHost", StringComparison.OrdinalIgnoreCase);
        }

        public FileAccessEvent[] GetRecentEvents()
        {
            return _events.ToArray();
        }

        public void Clear()
        {
            while (_events.TryDequeue(out _)) { }
        }
    }
}
