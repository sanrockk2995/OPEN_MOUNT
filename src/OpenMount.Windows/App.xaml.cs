using System;
using System.Windows;
using OpenMount.Core.Manager;
using OpenMount.Core.Security;

namespace OpenMount.Windows
{
    public partial class App : System.Windows.Application
    {
        public static DriveManager Manager { get; } = new DriveManager();
        public static DpapiConfigStore ConfigStore { get; } = new DpapiConfigStore();

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        private async void Application_Exit(object sender, ExitEventArgs e)
        {
            try
            {
                await Manager.DisposeAsync();
            }
            catch { }
        }
    }
}
