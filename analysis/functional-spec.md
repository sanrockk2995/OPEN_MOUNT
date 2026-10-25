# Functional Specification: Independent Windows Cloud Drive Mounter

**Project Codename:** OpenMount (Independent RaiDrive Alternative)  
**Target Environment:** Windows 10 & 11 x64  
**Primary Integration:** WinFsp (Windows File System Proxy)  
**Architecture:** Modular, Clean-Room Implementation  

---

## 1. System Overview

OpenMount provides user-mode virtual filesystem drivers that mount remote cloud storage as real, high-performance Windows drive letters (e.g. `X:`, `Y:`, `Z:`). Applications such as Windows Explorer, Office, media players, and command-line utilities interact with remote files seamlessly without requiring upfront synchronization of the entire remote dataset.

---

## 2. Component Architecture

```
+-------------------------------------------------------------------------+
|                          OpenMount System Tray / UI                     |
|         (WPF / Windows Desktop or CLI, Drive Dashboard, Settings)       |
+-------------------------------------------------------------------------+
                                    |
                                    v
+-------------------------------------------------------------------------+
|                           Drive Manager Service                         |
|   - Drive letter assignment (QueryDosDevice, GetLogicalDrives)          |
|   - Mount lifecycle management (Mount, Unmount, Remount on sleep/resume)|
|   - Dual-token UAC bridging (DefineDosDeviceW in session tokens)        |
|   - Windows Shell Notifications (SHChangeNotify)                        |
+-------------------------------------------------------------------------+
          |                                                 |
          v                                                 v
+------------------------------------+    +-------------------------------+
|      WinFsp Filesystem Layer       |    |     Configuration & Cache     |
| - Fsp.Interop / WinFsp Native C-API|    | - DPAPI Encrypted Store       |
| - Virtual File & Dir Descriptors   |    | - Read Cache (Block / Sparse) |
| - Read / Write / Rename / Delete   |    | - Write Staging & Upload Queue|
| - Directory Buffer Enumeration     |    | - Metadata SQLite / Memory DB |
+------------------------------------+    +-------------------------------+
                                    |
                                    v
+-------------------------------------------------------------------------+
|                         Storage Provider Interface                      |
|                  (IStorageProvider & Provider Registry)                 |
+-------------------------------------------------------------------------+
       |                           |                          |
       v                           v                          v
 [Local Provider]         [S3 / S3-Compatible]        [WebDAV / Nextcloud]
 (MVP & Testing)          (AWS, MinIO, Wasabi)        (Standard Protocol)
```

---

## 3. Storage Provider Interface (`IStorageProvider`)

All storage backends must implement the unified provider contract:

```csharp
public interface IStorageProvider : IAsyncDisposable
{
    string ProviderId { get; }
    string DisplayName { get; }

    Task InitializeAsync(CancellationToken ct);
    Task<StorageVolumeInfo> GetVolumeInfoAsync(CancellationToken ct);
    Task<StorageItemMetadata?> GetMetadataAsync(string path, CancellationToken ct);
    Task<IReadOnlyList<StorageItemMetadata>> ListDirectoryAsync(string path, CancellationToken ct);
    
    Task<Stream> OpenReadAsync(string path, long offset, long? length, CancellationToken ct);
    Task CreateFileAsync(string path, Stream contentStream, CancellationToken ct);
    Task CreateDirectoryAsync(string path, CancellationToken ct);
    Task DeleteFileAsync(string path, CancellationToken ct);
    Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken ct);
    Task MoveAsync(string sourcePath, string destinationPath, CancellationToken ct);
}
```

---

## 4. WinFsp Virtual Filesystem Semantics

1. **Volume Initialization:**
   - Filesystem Name: `OpenMount`
   - Volume Prefix: `\Device\Volume{GUID}\OpenMount\<DriveName>`
   - Attributes: Case-preserving, Case-insensitive, Unicode on disk, Network volume (`VolumeParams.Flags`).

2. **File Node Context:**
   - In-memory node hierarchy indexing file paths, sizes, attributes, and modified timestamps.
   - Node handle caching to avoid excessive roundtrips during Explorer thumbnailing or folder opening.

3. **Read Workflow:**
   - Application requests `Read(offset, length)`.
   - The filesystem layer queries the local cache manager.
   - If block is cached, return immediately from cache memory/disk.
   - If uncached, retrieve byte range from provider via HTTP Range or stream seek, write to cache, and serve to application.

4. **Write Workflow:**
   - On `Create` / `Write`, allocate temporary local buffer file in cache directory (`uploads/<file_id>.tmp`).
   - Route `Write` calls into the staging file and record dirty high-water mark.
   - On `Cleanup` (all handles closed), submit the staged file to the background upload queue.
   - If file was read-only or no writes occurred, cleanly close without triggering an upload.

---

## 5. Security & Credential Protection

- **No plaintext credentials:** Passwords, API secret keys, and OAuth access/refresh tokens are never stored in plaintext on disk or in logs.
- **Windows DPAPI Integration:** Configuration is serialized to JSON and encrypted using Windows Data Protection API (`ProtectedData.Protect(data, optionalEntropy, DataProtectionScope.CurrentUser)`).
- **Environment Isolation:** User configurations are stored per-user under `%APPDATA%\OpenMount\drives.json.enc`.

---

## 6. UAC & Explorer Shell Compatibility

- When mounting a drive letter (e.g. `Z:`), the drive must be visible to both standard user processes and elevated administrator processes.
- Implement `DefineDosDeviceW(DDD_RAW_TARGET_PATH, driveLetter, volumeDevicePath)` with proper error handling.
- Notify the Windows shell via `SHChangeNotify(SHCNE_DRIVEADD, SHCNF_PATHW, "Z:\\", IntPtr.Zero)` and `SHCNE_UPDATEITEM`.
- On unmount, call `DefineDosDeviceW(DDD_REMOVE_DEFINITION | DDD_EXACT_MATCH_ON_REMOVE, driveLetter, volumeDevicePath)` and `SHChangeNotify(SHCNE_DRIVEREMOVED, ...)`.
