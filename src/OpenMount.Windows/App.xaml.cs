using System;
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
            _instanceMutex = new Mutex(true, MutexName, out bool isNewInstance);
            if (!isNewInstance)
            {
                // Another instance already running: bring it to foreground and exit
                PostMessage((IntPtr)HWND_BROADCAST, WM_SHOW_OPENMOUNT, IntPtr.Zero, IntPtr.Zero);
                Current.Shutdown();
                return;
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
