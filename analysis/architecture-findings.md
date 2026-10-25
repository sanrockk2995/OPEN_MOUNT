# RaiDrive Mount Analysis - Architecture Findings

**Target:** RaiDrive Mount 2026.10.0 (x64)  
**Evidence Source:** Static disassembly (REA managed providers), metadata token analysis, P/Invoke boundary extraction, registry inspection, and runtime log analysis.

---

## 1. Process & Session Architecture

RaiDrive uses a multi-tier client-service architecture to satisfy Windows session isolation and security requirements:

```
+-------------------------------------------------------------------------------+
| SYSTEM CONTEXT (Session 0)                                                    |
|                                                                               |
|   [RaiDrive.Mount.Service.x64.exe] (Windows Service: RaiDriveMountService)     |
|     - Starts automatically with Windows                                       |
|     - Monitors user login via WTSGetActiveConsoleSessionId                    |
|     - Spawns session worker per interactive user via WTSQueryUserToken +       |
|       CreateProcessAsUser                                                     |
+-------------------------------------------------------------------------------+
                                      |
                                      v Spawns into User Session
+-------------------------------------------------------------------------------+
| USER CONTEXT (Session 1, 2, ...)                                              |
|                                                                               |
|   [RaiDrive.Mount.Service.x64.exe --session <ID>] (User Session Worker)       |
|     - Hosts SQLite local metadata cache                                       |
|     - Executes WinFsp C API callbacks (Fsp.Interop.FileSystemInterface)       |
|     - Performs cloud storage provider requests (S3, Google Drive, WebDAV, etc)|
|     - Exposes Named Pipe / SignalR IPC endpoint for UI                        |
|                                                                               |
|   [RaiDrive.Mount.exe] (Desktop WPF UI)                                       |
|     - Tray icon, drive management UI, settings window                         |
|     - WebView2 for interactive OAuth authorization                            |
|     - Sends Mount / Unmount / Add / Edit commands via MessagePack IPC         |
|                                                                               |
|   [RaiDrive.NotifyHelper.exe] (Invoked dual-scope: User + Elevated Admin)     |
|     - Solves UAC split-token drive visibility: maps Z: in both token spaces    |
|     - Calls DefineDosDeviceW and broadcasts SHChangeNotify                    |
+-------------------------------------------------------------------------------+
```

### Key Finding: UAC Split-Token Drive Letter Bridging
When UAC is active on Windows, an administrator user possesses two access tokens:
1. Filtered (non-elevated) token for regular applications (Explorer).
2. Elevated token for administrative processes (Command Prompt as Admin).
Because DOS device mappings are per-LUID (Logon Session), mapping a drive in one token does not automatically expose it in the other unless `EnableLinkedConnections` is set or the mapping is executed in both contexts.
**Evidence:** `service.log` shows:
```
<Notify.1.1> : Run : start : sessionId=1 : scope=User
<Notify.1.2> : Run : start : sessionId=1 : scope=Admin
<Notify.1.1> : DriveMap : Start : [Z:] : [\Device\Volume{GUID}\...]
<Notify.1.2> : DriveMap : Start : [Z:] : [\Device\Volume{GUID}\...]
```
RaiDrive explicitly spawns `NotifyHelper.exe` twice per mount: once in the standard user token, and once with the elevated token (if available), ensuring drive letters are accessible everywhere.

---

## 2. WinFsp Virtual Filesystem Integration

### P/Invoke Binding Structure
Rather than relying on high-level managed FUSE wrappers that incur overhead, RaiDrive directly P/Invokes `winfsp-x64.dll` exports via internal class `Fsp.Interop.Api` with dynamic binding (`LoadLibraryW` / `GetProcAddress`).

### Filesystem Interface Callbacks (`Fsp.Interop.FileSystemInterface`)
The filesystem operations map directly to the WinFsp user-mode filesystem dispatcher:

| WinFsp Callback | Cloud Storage Mapping |
|---|---|
| `GetVolumeInfo` | Reports total capacity, free space, volume label, and filesystem name ("RaiDrive" or provider name) |
| `SetVolumeLabel` | Updates local volume display name |
| `GetSecurityByName` | Returns default synthetic DACL granting standard permissions |
| `Create` / `CreateEx` | Allocates new file node, marks for upload upon close/flush |
| `Open` | Verifies existence in metadata cache or queries remote cloud provider; allocates file context |
| `Overwrite` / `OverwriteEx` | Truncates file node to 0 bytes, marks for upload |
| `Cleanup` | Invoked on handle dereference; triggers flush or final upload commit |
| `Close` | Releases in-memory handle, cleans up file context buffer |
| `Read` | Satisfies read via local cache segment; if uncached, issues HTTP range request (`bytes=offset-end`) |
| `Write` | Writes into local cache segment (`unsave/`), marks dirty ranges |
| `Flush` | Flushes pending writes to disk/cloud |
| `GetFileInfo` | Returns file size, attributes (File/Directory), creation time, last write time |
| `SetBasicInfo` | Updates timestamps or attributes |
| `SetFileSize` | Adjusts file length (for sparse or preallocated writes) |
| `CanDelete` | Validates delete permissions |
| `SetDelete` | Marks file for deletion upon handle close |
| `Rename` | Moves/renames file or directory on cloud remote |
| `ReadDirectory` | Enumerates directory entries using directory buffer (`FspFileSystemFillDirectoryBuffer`) |

### Mount Configuration Parameters (`VolumeParams`)
From `inspect_managed_members` on `Fsp.Interop.VolumeParams`:
- `CaseSensitiveSearch = 0` (Windows standard case-insensitive search).
- `CasePreservedNames = 1`.
- `UnicodeOnDisk = 1`.
- `PassQueryDirectoryPattern = 1` (WinFsp handles wildcards such as `*.*`).
- `PassQueryDirectoryFileName = 1`.
- `UmFileContextIsUserContext2 = 1`.
- `SectorSize = 4096`, `SectorsPerAllocationUnit = 1` (4KB cluster size).
- Volume mount string format: `\Device\Volume{GUID}\RaiDrive-<User>\<DriveName>`.

---

## 3. Storage Providers & Abstraction Layer

RaiDrive abstracts cloud storage behind a unified remote drive controller (`DRVCTRL`):
- **Object Storage (S3 / Compatible):** Uses official `AWSSDK.S3` client with bucket prefixing and multipart upload for large objects.
- **Protocols (FTP / SFTP / WebDAV):** Uses `Rebex` client suite for connection reuse, keep-alive, streaming, and chunked uploads.
- **OAuth Providers (Google Drive, OneDrive, Dropbox):** Uses REST APIs with standard authorization code flow with PKCE (`WebView2`), periodic token refresh (`Token Refreshed: 3599`), and delta change tracking (`BGENUM`).

---

## 4. Cache Architecture & Data Flow

RaiDrive employs a multi-tiered file caching system stored per-drive at:
`C:\ProgramData\OpenBoxLab\RaiDrive Mount\Cache\<DriveGUID>\`

```
Cache/<DriveGUID>/
  ├── data/       # File chunks / cached byte ranges
  ├── files/      # Fully downloaded or cached file staging
  ├── sysfiles/   # Internal tracking tables and lock files
  └── unsave/     # Dirty write buffers pending upload
```

### Read Operations:
1. Application requests `Read(offset, length)`.
2. Check if bytes exist in `files/` or `data/` chunks.
3. If hit: Serve immediately from local disk (zero network latency).
4. If miss: Issue HTTP/HTTPS range request (`Range: bytes=start-end`) to the cloud provider, stream bytes into local cache, and return data to WinFsp.

### Write Operations:
1. Application requests `Write(offset, buffer)`.
2. Data is written to `unsave/` local staging buffer.
3. On `Cleanup` / `Close`: Asynchronous upload worker queues the file for upload.
4. For small files: direct PUT / upload.
5. For large files: Multipart / chunked upload (e.g. 8MB-32MB parts).
6. Upon successful upload, file moves from `unsave/` to `files/` or cache.

---

## 5. Persistent Configuration & Credentials

1. **Drive Configurations:**
   - Stored in registry: `HKCU\Software\OpenBoxLab\RaiDrive Mount\Drives\<DriveGUID>`.
   - Payload is encrypted with **Windows DPAPI** (`ProtectedData.Protect` with CurrentUser scope).
   - Contains: Provider type, mount letter, remote path, credentials / OAuth tokens, cache policy, read-only flag.

2. **Security & Machine Binding:**
   - On first run, creates an ECDSA P-256 non-exportable key in Windows CNG / MachineKey store (`DPoP : Device Key Created`).
   - Protects registry ACLs against tampering (`SecureRegistryAclMonitor`).
