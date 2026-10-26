using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using OpenMount.Core.Manager;
using OpenMount.Core.Security;

namespace OpenMount.Windows
{
    public partial class App : System.Windows.Application
    {
        public static DriveManager Manager { get; } = new DriveManager();
        public static DpapiConfigStore ConfigStore { get; } = new DpapiConfigStore();

        private static Mutex? _instanceMutex;
        private const string MutexName = "Local\\OpenMount_SingleInstance_Mutex";

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const int HWND_BROADCAST = 0xffff;
        public static readonly uint WM_SHOW_OPENMOUNT = RegisterWindowMessage("WM_SHOW_OPENMOUNT_EVENT");

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // Global crash protection & error logging
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                LogCrash(args.ExceptionObject?.ToString() ?? "Unknown domain error");
            };

            DispatcherUnhandledException += (s, args) =>
            {
                LogCrash(args.Exception.ToString());
                System.Windows.MessageBox.Show(
                    $"OpenMount encountered an unexpected error:\n{args.Exception.Message}",
                    "OpenMount Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
            };

            bool isNewInstance = false;
            try
            {
                _instanceMutex = new Mutex(true, MutexName, out isNewInstance);
            }
            catch (AbandonedMutexException)
            {
                isNewInstance = true;
            }

            if (!isNewInstance)
            {
                // Double check if another OpenMount process is actually alive
                int currentPid = Environment.ProcessId;
                var otherProcesses = System.Diagnostics.Process.GetProcesses()
                    .Where(p => (p.ProcessName.Equals("OpenMount", StringComparison.OrdinalIgnoreCase) ||
                                 p.ProcessName.Equals("OpenMount.Windows", StringComparison.OrdinalIgnoreCase)) &&
                                 p.Id != currentPid)
                    .ToList();

                if (otherProcesses.Count > 0)
                {
                    // Existing instance running: wake it up and exit
                    PostMessage((IntPtr)HWND_BROADCAST, WM_SHOW_OPENMOUNT, IntPtr.Zero, IntPtr.Zero);
                    Current.Shutdown();
                    return;
                }
            }

            try
            {
                ThemeManager.Initialize();
            }
            catch (Exception ex)
            {
                LogCrash($"ThemeManager initialization error: {ex}");
            }

            bool startMinimized = e.Args.Any(a =>
                string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "-m", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "/minimized", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            if (startMinimized)
            {
                mainWindow.WindowState = WindowState.Minimized;
                mainWindow.Hide();
            }
            else
            {
                mainWindow.Show();
            }
        }

        private static void LogCrash(string message)
        {
            try
            {
                string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenMount");
                Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, "crash.log");
                File.AppendAllText(logFile, $"[{DateTime.UtcNow:u}] {message}\n\n");
            }
            catch { }
        }

        private async void Application_Exit(object sender, ExitEventArgs e)
        {
            try
            {
                await Manager.DisposeAsync();
            }
            catch { }

            try
            {
                _instanceMutex?.ReleaseMutex();
                _instanceMutex?.Dispose();
            }
            catch { }
        }
    }
}
