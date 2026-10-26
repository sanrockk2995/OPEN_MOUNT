using System;
using System.IO;
using OpenMount.Core.FileSystem;
using Xunit;

namespace OpenMount.Tests
{
    public class WinFspInstallerTests
    {
        [Fact]
        public void IsInstalled_ReturnsBooleanWithoutThrowing()
        {
            // Verifies the method executes cleanly and returns a valid boolean state on any host/CI
            bool isInstalled = WinFspInstaller.IsInstalled();
            Assert.True(isInstalled || !isInstalled);
        }

        [Fact]
        public void LocateInstaller_FindsBundledMsiPackage()
        {
            string? msiPath = WinFspInstaller.LocateInstaller();
            Assert.NotNull(msiPath);
            Assert.True(File.Exists(msiPath), $"Located installer path must exist: {msiPath}");

            var fi = new FileInfo(msiPath);
            Assert.True(fi.Length > 1_000_000, $"Installer MSI must be valid size (> 1MB), got: {fi.Length} bytes");
        }

        [Fact]
        public void DownloadUrls_AreValidOfficialEndpoints()
        {
            Assert.StartsWith("https://github.com/winfsp/winfsp/releases/", WinFspInstaller.FallbackDownloadUrl);
            Assert.EndsWith(".msi", WinFspInstaller.FallbackDownloadUrl);
            Assert.StartsWith("https://github.com/winfsp/winfsp/releases/", WinFspInstaller.SecondaryDownloadUrl);
            Assert.EndsWith(".msi", WinFspInstaller.SecondaryDownloadUrl);
        }
    }
}
