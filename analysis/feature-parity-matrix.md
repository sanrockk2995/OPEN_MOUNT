# Feature Parity Matrix: RaiDrive vs. OpenMount

| Functional Area | Feature / Capability | RaiDrive 2026.10 (Observed) | OpenMount Independent (MVP + Target) | Status / Implementation Strategy |
|---|---|---|---|---|
| **OS & Kernel Integration** | Virtual Filesystem Driver | WinFsp 2026 (`winfsp-x64.dll`) | WinFsp 2026 (`winfsp-x64.dll`) | Complete Parity |
| | Drive Letter Assignment | `DefineDosDeviceW` | `DefineDosDeviceW` | Complete Parity |
| | UAC Split-Token Visibility | Spawns `NotifyHelper.exe` dual-token | Multi-token mapping or service helper | Complete Parity |
| | Shell Notifications | `SHChangeNotify` / `BroadcastSystemMessage` | `SHChangeNotify` | Complete Parity |
| | Filesystem Type | NTFS / FAT Network Emulation | Network or Local Disk Emulation | Complete Parity |
| **Filesystem Semantics** | File Creation & Deletion | Supported | Supported | MVP Core |
| | Sequential & Random Read | Supported (Range request chunking) | Supported (Block cache + Range stream) | MVP Core |
| | File Writing & Appending | Staging file + asynchronous upload | Staging file + upload queue | MVP Core |
| | File Renaming & Moving | Supported | Supported | MVP Core |
| | Directory Enumeration | Cached with background refresh | In-memory tree with TTL cache | MVP Core |
| | File Locking | Supported | File handle tracking | MVP Core |
| **Storage Providers** | Local / Directory Loopback | N/A (Cloud only) | Supported (MVP Verification) | OpenMount MVP |
| | Amazon S3 / S3-Compatible | Supported (`AWSSDK.S3`) | Supported (S3 SDK / REST API) | Phase 2 |
| | WebDAV / Nextcloud | Supported (`Rebex.Http`) | Supported (`System.Net.Http`) | Phase 2 |
| | SFTP / FTP | Supported (`Rebex.Sftp` / `Ftp`) | Supported (SSH.NET / FluentFTP) | Planned |
| | Google Drive / OneDrive | Supported (OAuth2 PKCE) | Supported (OAuth2 PKCE / REST) | Planned |
| **Caching & Performance** | Read-ahead Chunking | Supported (`Cache/<GUID>/data`) | Block-based sparse cache | MVP Core |
| | Write Buffering | Supported (`Cache/<GUID>/unsave`) | Temp file buffer (`uploads/`) | MVP Core |
| | Local Metadata Index | SQLite database | In-memory + SQLite index | Complete Parity |
| | Offline Attribute Support | Supported | Supported | Complete Parity |
| **Security & Privacy** | Credential Storage | Windows DPAPI in Registry | Windows DPAPI in local secure JSON | Complete Parity |
| | Machine Binding | CNG ECDSA P-256 Device Key | Windows CNG / DPAPI | Complete Parity |
| | Zero-Token Logging | Enforced | Enforced | Complete Parity |
| **Management & Packaging** | Packaging | .NET Single-File Host | .NET / Native standalone binary | Complete Parity |
| | CLI Management | Service controls | Interactive / Scriptable CLI + Tray | Enhanced |
