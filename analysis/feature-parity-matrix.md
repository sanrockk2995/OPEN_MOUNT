# Feature Parity Matrix: RaiDrive vs. OpenMount

| Functional Area | Feature / Capability | RaiDrive 2026.10 (Observed) | OpenMount Status | Implementation & Verification Evidence |
|---|---|---|---|---|
| **OS & Kernel Integration** | Virtual Filesystem Driver | WinFsp 2026 (`winfsp-x64.dll`) | **Complete Parity** | `Fsp.FileSystemBase` in `VirtualFileSystem.cs`, verified via WinFsp runtime |
| | Drive Letter Assignment | `DefineDosDeviceW` | **Complete Parity** | `DosDeviceHelper.cs` P/Invoke `DefineDosDeviceW` |
| | UAC Split-Token Visibility | Spawns `NotifyHelper.exe` dual-token | **Complete Parity** | `DosDeviceHelper.cs` + Session-level Drive Manager |
| | Shell Notifications | `SHChangeNotify` / `BroadcastSystemMessage` | **Complete Parity** | `SHChangeNotify` (`SHCNE_DRIVEADD`, `SHCNE_DRIVEREMOVED`) |
| | Filesystem Security | Shared ACL | **Enhanced (Least Privilege)** | Dynamic SDDL granting Full Control strictly to Caller SID, `SY`, and `BA` |
| **Filesystem Semantics** | File Creation & Deletion | Supported | **Complete Parity** | `VirtualFileSystem.Create`, `DeleteFileAsync`, `DeleteDirectoryAsync` |
| | Sequential & Random Read | Supported (Range request chunking) | **Complete Parity** | Range request streaming (`OpenReadAsync`) across S3 & Google Drive |
| | File Writing & Appending | Staging file + asynchronous upload | **Complete Parity** | Local staging stream committed on `Cleanup` |
| | Random-Access Seek & Multi-Handle | Supported | **Complete Parity** | Concurrent `FileStream` handles with `FileShare.ReadWrite`, verified via `Test_EndToEnd_RandomAccessSeekAndMultipleHandles` |
| | Inode Sharing | Supported | **Complete Parity** | Thread-safe `_activeNodes` registry (`Test_ConcurrentHandles_ShareSameNode`) |
| | Zero Data Loss Quarantine | Basic temp cache | **Enhanced (Quarantine)** | Auto-copies modified uncommitted drafts to `%APPDATA%\OpenMount\unsaved` on upload failure (`Test_FailedUpload_PreservesStagedDataInRecoveryDirectory`) |
| | File Renaming & Moving | Supported | **Complete Parity** | Atomic move with parent folder ID reassignment |
| | Directory Enumeration | Cached with background refresh | **Complete Parity** | In-memory directory cache with synthesized `.` and `..` |
| **Storage Providers** | Local Folder Loopback | N/A (Cloud only) | **Verified Working** | `LocalStorageProvider.cs`, 5 dedicated unit tests |
| | Amazon S3 / MinIO / R2 | Supported (`AWSSDK.S3`) | **Verified Working** | `S3StorageProvider.cs`, ByteRange streaming, 4 tests |
| | Google Drive API v3 | Supported (OAuth2 PKCE) | **Verified Working** | Official Drive API v3, PKCE loopback, Range streaming, 4 tests |
| | Google Resumable Uploads | Supported (1 MB - 8 MB chunks) | **Complete Parity** | 1 MB chunking with exponential backoff `ResumeAsync` on transient 5xx/429 errors (`Test_ResumableUpload_InitiationAndProtocolHeaders`) |
| | Google Workspace Docs | Supported (Read-only export) | **Verified Working** | Auto-export Docs to DOCX, Sheets to XLSX, Slides to PPTX |
| | Duplicate Filename Handling | Supported | **Verified Working** | Deterministic disambiguation (`photo (2).jpg`) for Explorer |
| | WebDAV / Nextcloud | Supported (`Rebex.Http`) | Planned (Phase 2) | Interface ready in `IStorageProvider` |
| | SFTP / FTP | Supported (`Rebex.Sftp` / `Ftp`) | Planned (Phase 2) | Interface ready in `IStorageProvider` |
| **Security & Privacy** | Credential Storage | Windows DPAPI in Registry | **Enhanced (DPAPI)** | `DpapiConfigStore.cs` & `DpapiDataStore.cs` under `CurrentUser` |
| | OAuth Token Store | Encrypted file / Registry | **Complete Parity** | `DpapiDataStore.cs` encrypts Google OAuth tokens on disk |
| | Zero-Token Logging | Enforced | **Complete Parity** | Strict redaction, zero plaintext tokens in logs |
| **User Interface & Management** | Desktop UI | WPF (`MahApps.Metro`) | **Complete Parity** | Modern WPF dashboard (`OpenMount.Windows`) |
| | System Tray Integration | Tray icon with quick connect | **Complete Parity** | Native Windows `NotifyIcon` with minimize-to-tray & context menu |
| | Add-Drive Wizard | Custom WPF Wizard | **Complete Parity** | `AddEditDriveDialog.xaml` with auto-detected free drive letters |
| | CLI Management | Service controls | **Enhanced (Dual Mode)** | Full CLI runner (`OpenMount.Cli`) + Desktop UI (`OpenMount.Windows`) |
| | Auto-Mount on Startup | Registry Run key | **Complete Parity** | Profile auto-mount setting loaded on application startup |
