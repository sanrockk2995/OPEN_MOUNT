using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OpenMount.Core.FileSystem
{
    public static class WinFspInstaller
    {
        public const string FallbackDownloadUrl = "https://github.com/winfsp/winfsp/releases/download/v2.2B4/winfsp-2.2.26215.msi";
        public const string SecondaryDownloadUrl = "https://github.com/winfsp/winfsp/releases/download/v2.1/winfsp-2.1.25156.msi";

        /// <summary>
        /// Checks whether the WinFsp kernel driver and runtime libraries are installed on the system.
        /// </summary>
        public static bool IsInstalled()
        {
            try
            {
                // 1. Check WOW6432Node registry key
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\WinFsp"))
                {
                    if (key?.GetValue("InstallDir") is string dir && Directory.Exists(dir))
                    {
                        if (File.Exists(Path.Combine(dir, "bin", "winfsp-x64.dll")) ||
                            File.Exists(Path.Combine(dir, "bin", "winfsp-x86.dll")))
                        {
                            return true;
                        }
                    }
                }

                // 2. Check 64-bit native registry key
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WinFsp"))
                {
                    if (key?.GetValue("InstallDir") is string dir && Directory.Exists(dir))
                    {
                        if (File.Exists(Path.Combine(dir, "bin", "winfsp-x64.dll")) ||
                            File.Exists(Path.Combine(dir, "bin", "winfsp-x86.dll")))
                        {
                            return true;
                        }
                    }
                }

                // 3. Check well-known Program Files locations
                string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (File.Exists(Path.Combine(progFilesX86, "WinFsp", "bin", "winfsp-x64.dll")))
                {
                    return true;
                }

                string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (File.Exists(Path.Combine(progFiles, "WinFsp", "bin", "winfsp-x64.dll")))
                {
                    return true;
                }

                // 4. Check kernel service registration
                using (var svcKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\winfsp"))
                {
                    if (svcKey != null)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Access check fallback
            }

            return false;
        }

        /// <summary>
        /// Locates a bundled WinFsp MSI installer from redist directories or parent paths.
        /// </summary>
        public static string? LocateInstaller()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string curDir = Directory.GetCurrentDirectory();

            string[] candidatePaths = new[]
            {
                Path.Combine(baseDir, "redist", "winfsp.msi"),
                Path.Combine(baseDir, "winfsp.msi"),
                Path.Combine(curDir, "redist", "winfsp.msi"),
                Path.Combine(curDir, "winfsp.msi"),
                // When running under Visual Studio or dotnet run (bin/Debug/net8.0-windows/...)
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\redist\winfsp.msi")),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\redist\winfsp.msi")),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\redist\winfsp.msi")),
                Path.GetFullPath(Path.Combine(curDir, @"..\redist\winfsp.msi")),
                Path.GetFullPath(Path.Combine(curDir, @"..\..\redist\winfsp.msi"))
            };

            foreach (var path in candidatePaths)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        var info = new FileInfo(path);
                        if (info.Length > 100_000) // Valid MSI file size check (> 100 KB)
                        {
                            return path;
                        }
                    }
                }
                catch { }
            }

            // Search for any winfsp*.msi in redist directory if named differently
            try
            {
                string redistDir = Path.Combine(baseDir, "redist");
                if (Directory.Exists(redistDir))
                {
                    var msiFiles = Directory.GetFiles(redistDir, "*winfsp*.msi");
                    if (msiFiles.Length > 0 && new FileInfo(msiFiles[0]).Length > 100_000)
                    {
                        return msiFiles[0];
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Downloads the official signed WinFsp installer from GitHub if no local installer exists.
        /// </summary>
        public static async Task<string> DownloadInstallerAsync(CancellationToken ct = default)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "OpenMount");
            Directory.CreateDirectory(tempDir);
            string destPath = Path.Combine(tempDir, "winfsp.msi");

            if (File.Exists(destPath) && new FileInfo(destPath).Length > 1_000_000)
            {
                return destPath;
            }

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenMount-Installer/1.0");

            string[] urls = new[] { FallbackDownloadUrl, SecondaryDownloadUrl };
            Exception? lastEx = null;

            foreach (var url in urls)
            {
                try
                {
                    using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                    using var sourceStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await sourceStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);

                    if (new FileInfo(destPath).Length > 500_000)
                    {
                        return destPath;
                    }
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }

            throw new InvalidOperationException($"Failed to download WinFsp installer from official release mirrors: {lastEx?.Message}", lastEx);
        }

        /// <summary>
        /// Installs WinFsp automatically via msiexec with elevated privileges (UAC prompt).
        /// </summary>
        public static async Task<(bool Success, string Message)> InstallAsync(bool silent = true, CancellationToken ct = default)
        {
            if (IsInstalled())
            {
                return (true, "WinFsp is already installed on this system.");
            }

            string? msiPath = LocateInstaller();
            if (string.IsNullOrEmpty(msiPath) || !File.Exists(msiPath))
            {
                try
                {
                    msiPath = await DownloadInstallerAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return (false, $"Unable to locate bundled WinFsp installer and online download failed: {ex.Message}");
                }
            }

            try
            {
                // msiexec /i "path\winfsp.msi" /qn /norestart
                // /qn = completely quiet, no UI
                // /passive = unattended, shows only progress bar
                string uiFlag = silent ? "/qn" : "/passive";
                string arguments = $"/i \"{msiPath}\" {uiFlag} /norestart";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "msiexec.exe",
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas" // Triggers Windows UAC elevation
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return (false, "Failed to start msiexec installer process.");
                }

                await process.WaitForExitAsync(ct).ConfigureAwait(false);

                // msiexec exit codes:
                // 0 = ERROR_SUCCESS
                // 3010 = ERROR_SUCCESS_REBOOT_REQUIRED
                // 1602 = ERROR_INSTALL_USEREXIT (user cancelled UAC)
                if (process.ExitCode == 0 || process.ExitCode == 3010)
                {
                    // Brief pause to allow driver registry keys and service state to initialize
                    for (int i = 0; i < 10; i++)
                    {
                        await Task.Delay(500, ct).ConfigureAwait(false);
                        if (IsInstalled())
                        {
                            return (true, "WinFsp installed successfully.");
                        }
                    }

                    return (true, "WinFsp installer completed successfully.");
                }

                if (process.ExitCode == 1602)
                {
                    return (false, "WinFsp installation was cancelled by user.");
                }

                return (false, $"WinFsp installation failed with msiexec exit code {process.ExitCode}.");
            }
            catch (System.ComponentModel.Win32Exception w32Ex) when (w32Ex.NativeErrorCode == 1223)
            {
                // ERROR_CANCELLED: The operation was canceled by the user (UAC prompt denied)
                return (false, "Administrator permissions were denied (UAC cancelled).");
            }
            catch (Exception ex)
            {
                return (false, $"Error installing WinFsp: {ex.Message}");
            }
        }
    }
}
