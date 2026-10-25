# RaiDrive Mount Analysis - Inventory Report

**Target Version:** 2026.10.0 (x64)  
**Publisher:** OpenBoxLab Inc.  
**Platform:** Windows 10/11 x64  
**Analysis Date:** 2026-10-25  

---

## 1. Primary Binaries & Artifacts

| Binary Path | Format / Subsystem | Size (Bytes) | SHA-256 | Purpose |
|---|---|---|---|---|
| `C:\Program Files\OpenBoxLab\RaiDrive Mount\RaiDrive.Mount.Service.x64.exe` | PE32+ (x86_64) GUI/CUI Service | 18,825,296 | `70f947cc639576f55808741005996d8b82c7619c31fee6d5c0f6f4b83171846b` | Background Windows Service & Session Engine |
| `C:\Program Files\OpenBoxLab\RaiDrive Mount\RaiDrive.Mount.exe` | PE32+ (x86_64) Windows GUI | 31,897,224 | `ec4ae608fb0943de94d213e76c4e726e3b2bf3b790772a0a9735b1db210b3263` | WPF Desktop UI & User Experience |
| `C:\Program Files\OpenBoxLab\RaiDrive Mount\RaiDrive.NotifyHelper.exe` | PE32+ (x86_64) Windows GUI | 283,920 | `ea00430c97bdd0201d9a09e1c0b371c5ba324eca4ae48a4a3c8954541518853f` | Session / UAC Token Bridge & Drive Notification |
| `C:\Program Files\OpenBoxLab\RaiDrive Mount\RaiDrive.ShellExtension.x64.dll` | PE32+ (x86_64) DLL | 116,240 | (Verified) | Windows Explorer Shell Extension |
| `C:\Program Files\OpenBoxLab\RaiDrive Mount\driver\winfsp.msi` | Windows Installer Package | 2,207,744 | (Verified) | WinFsp 2026 Distribution Package |

---

## 2. Runtime Packaging & Deployment Format

Static analysis via REA (`inspect_managed_artifact`) confirmed that both `RaiDrive.Mount.Service.x64.exe` and `RaiDrive.Mount.exe` are packaged using the modern **.NET Single-File Host (AppHost)** targeting `.NETCoreApp,Version=v10.0`.
- Outer host: Native PE32+ executable stub embedding the CLR bootstrap logic.
- Inner payload: Embedded managed PE/CLI assemblies extracted at offset boundaries.

### Extracted Core Assemblies

| Assembly Name | Target Framework | Managed Tokens | Primary Role |
|---|---|---|---|
| `RaiDrive.Mount.Service.x64.dll` | .NET 10.0 (`v4.0.30319`) | 18,233 MethodDefs, 1,556 TypeDefs | Filesystem virtualization, WinFsp C-API interop, storage providers, cache & SQLite management |
| `RaiDrive.Mount.dll` | .NET 10.0 (`v4.0.30319`) | 11,179 MethodDefs, 1,243 TypeDefs | WPF Modern UI, CommunityToolkit.Mvvm view models, WebView2 OAuth flows, SignalR client |
| `RaiDrive.NotifyHelper.dll` | .NET 10.0 (`v4.0.30319`) | ~80 MethodDefs | Session-specific DosDevice mapping (`DefineDosDeviceW`) and shell broadcast (`SHChangeNotify`) |

---

## 3. Key Dependencies & Third-Party Components

1. **Filesystem Engine:**
   - **WinFsp (Windows File System Proxy):** `winfsp-x64.dll` and `winfsp-x64.sys`.
   - Interaction method: Dynamic P/Invoke wrapper (`Fsp.Interop.Api`) over native C exports.

2. **Storage Provider Libraries:**
   - `AWSSDK.S3.dll` & `AWSSDK.Core.dll`: Amazon S3, MinIO, Wasabi, Backblaze B2, Ceph, Google Cloud Storage (S3-compatible).
   - `Rebex.Ftp.dll`, `Rebex.Sftp.dll`, `Rebex.Http.dll`, `Rebex.Networking.dll`: Robust implementation of FTP/FTPS, SFTP, and WebDAV protocols.
   - Built-in REST / HTTP client: Google Drive, Microsoft OneDrive, Dropbox, Box, Google Workspace.

3. **Data Storage & Serialization:**
   - `Microsoft.Data.Sqlite.dll` & `SQLitePCLRaw.*`: Embedded SQLite engine for metadata and cache indexing.
   - `MessagePack.dll` & `MessagePack.Annotations.dll`: High-speed binary IPC serialization between GUI and Service.
   - `Newtonsoft.Json.dll` & `System.Text.Json`: Configuration and REST API interchange.

4. **UI & Presentation:**
   - `PresentationFramework.dll`, `PresentationCore.dll`: Windows Presentation Foundation (WPF).
   - `MahApps.Metro.dll` & `ControlzEx.dll`: Modern Windows UI controls and themes.
   - `Microsoft.Web.WebView2.Wpf.dll`: Chromium-based WebView2 for OAuth2 browser authentication.
   - `CommunityToolkit.Mvvm.dll`: Reactive MVVM architecture.

5. **Security & Cryptography:**
   - `BouncyCastle.Cryptography.dll` & `System.Security.Cryptography.ProtectedData`: Local Windows DPAPI for token and configuration persistence.
   - ECDSA P-256 device key generation for client-to-service mutual authentication.
