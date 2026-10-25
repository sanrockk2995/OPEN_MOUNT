# OpenMount: High-Performance Independent RaiDrive Alternative

OpenMount is a clean-room, production-grade Windows application and virtual filesystem engine that mounts local directories and cloud storage providers (such as Amazon S3, MinIO, Cloudflare R2, Backblaze B2, and local folders) as native Windows drive letters (e.g., `Z:`), backed by the **WinFsp** kernel driver.

Developed using evidence-based reverse-engineering insights from commercial cloud storage mounters (RaiDrive) via the **REA (Reverse Engineer Anything)** methodology.

---

## 1. Architectural Highlights

```
+-------------------------------------------------------------+
|                  Windows File Explorer / Apps               |
|            (Word, Excel, PowerShell, Video Players)         |
+-------------------------------------------------------------+
                              |
                     Standard Win32 / NT I/O
                              |
+-------------------------------------------------------------+
|               WinFsp Kernel Driver (winfsp.sys)             |
+-------------------------------------------------------------+
                              |
                       FSP FSCTL Interop
                              |
+-------------------------------------------------------------+
|         OpenMount.Core Engine (Fsp.FileSystemBase)          |
|  - VirtualFileSystem: POSIX/Win32 file operation mapping    |
|  - VirtualFileNode / VirtualFileDesc: Handle tracking       |
|  - Staging Engine: Local write-buffer & commit on Cleanup   |
|  - DosDeviceHelper: DefineDosDeviceW & SHChangeNotify       |
+-------------------------------------------------------------+
                              |
                   IStorageProvider Pipeline
           +------------------+------------------+
           |                                     |
+----------------------+              +----------------------+
| LocalStorageProvider |              |  S3StorageProvider   |
| (Local folder mount) |              | (HTTP Range streams) |
+----------------------+              +----------------------+
```

### Key Differences & Improvements over Proprietary Mounters
1. **Zero Proprietary Drivers**: Uses standard, open, audited WinFsp runtime rather than bundled or unsigned legacy drivers.
2. **On-Demand Streaming (HTTP Range)**: Multi-gigabyte cloud media files stream instantly without full pre-downloading.
3. **Atomic Write Staging**: Files modified by Windows applications stage to temporary cache files and upload atomically upon handle `Cleanup`, preventing corrupt partial objects.
4. **Hardware-Backed Encryption (DPAPI)**: Cloud credentials, access keys, and drive profiles are encrypted via Windows Data Protection API (`ProtectedData.Protect` in `CurrentUser` scope). Zero plaintext secrets on disk.
5. **Session & UAC Broadcast**: Seamless drive availability across both elevated administrator and standard user sessions using `DefineDosDeviceW` and Windows Shell notifications (`SHChangeNotify`).

---

## 2. Directory Structure

```
D:\AI\MOUT\
├── OpenMount.sln                     # Visual Studio / .NET Solution
├── lib\                              # Local WinFsp MSIL assemblies
│   ├── winfsp-msil.dll
│   └── winfsp-msil.xml
├── src\
│   ├── OpenMount.Core\               # Core Virtual Filesystem & Storage Engine
│   │   ├── FileSystem\
│   │   │   ├── VirtualFileSystem.cs  # Implements WinFsp callbacks (Open, Read, Write, etc.)
│   │   │   ├── VirtualFileNode.cs    # In-memory inode and metadata tracking
│   │   │   └── VirtualFileDesc.cs    # File handle descriptor and staging buffer
│   │   ├── Manager\
│   │   │   ├── DriveManager.cs       # Mount / unmount orchestration
│   │   │   ├── DriveConfig.cs        # Drive configuration model
│   │   │   └── DosDeviceHelper.cs    # P/Invoke for Win32 device names and notifications
│   │   ├── Security\
│   │   │   └── DpapiConfigStore.cs   # DPAPI-encrypted persistent configuration
│   │   └── Storage\
│   │       ├── IStorageProvider.cs   # Generic async storage provider contract
│   │       ├── StorageItemMetadata.cs# File/directory metadata and attributes
│   │       ├── Local\
│   │       │   └── LocalStorageProvider.cs # Local folder backing provider
│   │       └── S3\
│   │           └── S3StorageProvider.cs    # AWS S3 / MinIO / R2 provider
│   ├── OpenMount.Cli\                # Command-line interface and daemon runner
│   │   └── Program.cs
│   └── OpenMount.Tests\              # Automated unit and integration test suite
│       ├── LocalStorageProviderTests.cs
│       ├── DpapiConfigStoreTests.cs
│       ├── VirtualFileSystemSemanticsTests.cs
│       ├── S3StorageProviderTests.cs
│       └── MountIntegrationTests.cs
├── analysis\                         # REA Reverse-Engineering Reports
│   ├── inventory.md
│   ├── architecture-findings.md
│   ├── functional-spec.md
│   ├── feature-parity-matrix.md
│   └── unknowns.md
└── publish\                          # Published standalone binaries
    └── OpenMount.Cli.exe
```

---

## 3. Prerequisites & Installation

### Requirements
- **OS**: Windows 10 / Windows 11 (x64)
- **Driver**: [WinFsp](https://winfsp.dev/) (version 2.0 or later installed)
- **SDK**: .NET 8.0 SDK (portable or installed)

### Build Instructions
Open PowerShell in the repository root:

```powershell
# Restore dependencies
dotnet restore OpenMount.sln

# Build solution
dotnet build OpenMount.sln -c Release

# Run automated test suite
dotnet test OpenMount.sln

# Publish self-contained CLI
dotnet publish src/OpenMount.Cli/OpenMount.Cli.csproj -c Release -o publish
```

---

## 4. Usage Guide

### Mounting a Local Folder as a Drive Letter
```cmd
openmount mount -l Z: -p local --path "D:\Media" --label "Local Virtual Drive"
```

### Mounting an S3 Cloud Storage Bucket
Works with AWS S3, Cloudflare R2, MinIO, or Backblaze B2:
```cmd
openmount mount -l Y: -p s3 --bucket my-cloud-bucket --endpoint https://s3.amazonaws.com --key AKIA... --secret ... --label "S3 Cloud Drive"
```

### Persisting Drive Configuration (Encrypted)
To save the configuration to the DPAPI store for auto-mounting:
```cmd
openmount mount -l Z: -p local --path "C:\Storage" --label "Work Storage" --save
```

### Managing Stored Configurations
```cmd
# List all saved drives
openmount config list
```

---

## 5. Automated Test Suite Results

Test suite verification executed via xUnit and .NET 8.0:

| Test Case | Component | Verification Target | Status |
| :--- | :--- | :--- | :--- |
| `Test_VolumeInfo_ReturnsValidSizes` | `LocalStorageProvider` | Total/Free volume space reporting | **PASSED** |
| `Test_CreateAndReadFile_RoundTrip` | `LocalStorageProvider` | File creation, UTF-8 reading roundtrip | **PASSED** |
| `Test_PartialRead_RangeSlice` | `LocalStorageProvider` | Sub-stream range slicing & offsets | **PASSED** |
| `Test_DirectoryOperations_CreateListDelete` | `LocalStorageProvider` | Hierarchical directories, moving, listing | **PASSED** |
| `Test_LargeFile_SequentialAndSeek` | `LocalStorageProvider` | 5 MB binary file seek & integrity check | **PASSED** |
| `Test_SaveAndLoadDrives_EncryptedRoundTrip` | `DpapiConfigStore` | DPAPI roundtrip & ciphertext inspection | **PASSED** |
| `Test_GetVolumeInfo_ReturnsSuccess` | `VirtualFileSystem` | WinFsp volume info interop | **PASSED** |
| `Test_GetSecurityByName_RootAndSubpath` | `VirtualFileSystem` | SDDL permissions & directory flags | **PASSED** |
| `Test_CreateWriteReadCleanup_RoundTrip` | `VirtualFileSystem` | Native memory buffers, unmanaged I/O | **PASSED** |
| `Test_ReadDirectoryEntry_IncludesDotAndDotDot` | `VirtualFileSystem` | Windows directory navigation (`.` and `..`) | **PASSED** |
| `Test_SetFileSize_TruncateAndExpand` | `VirtualFileSystem` | WinFsp file truncation & enlargement | **PASSED** |
| `Test_GetMetadataAsync_ExistingObject` | `S3StorageProvider` | S3 metadata mapping & ETag retention | **PASSED** |
| `Test_GetMetadataAsync_NotFound_ReturnsNull` | `S3StorageProvider` | 404 HTTP status code translation | **PASSED** |
| `Test_ListDirectoryAsync_ReturnsFilesAndPrefixes`| `S3StorageProvider` | CommonPrefixes and S3Object hierarchy | **PASSED** |
| `Test_OpenReadAsync_ByteRangeStreaming` | `S3StorageProvider` | HTTP Byte Range on-demand streaming | **PASSED** |
| `Test_EndToEnd_MountFilesystemOperationsAndUnmount` | Full Stack | Real WinFsp drive mount, Shell I/O, unmount | **PASSED** |

**Summary: 16 Passed, 0 Failed, 0 Skipped.**

---

## 6. Prioritized Roadmap for Complete Feature Parity

1. **Phase 1: Additional Cloud Storage Providers**
   - WebDAV (Nextcloud, ownCloud, Synology, QNAP)
   - Google Drive (OAuth2 PKCE flow + Drive API v3)
   - Microsoft OneDrive / SharePoint (Microsoft Graph API)
   - SFTP / FTP (SSH.NET integration)

2. **Phase 2: Advanced Caching & Offline Capabilities**
   - Read-ahead buffer for media streaming.
   - Persistent SQLite chunk index for local disk block caching.
   - Bandwidth rate limiter (configurable upload/download limits).

3. **Phase 3: Desktop UI & System Tray Integration**
   - WPF / WinUI 3 modern desktop interface.
   - System tray icon with quick connect/disconnect toggles.
   - Windows toast notifications for transfer status and authentication expiration.
