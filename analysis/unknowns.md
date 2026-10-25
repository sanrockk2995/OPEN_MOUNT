# Unknowns and Analysis Limitations Ledger

**Target:** RaiDrive Mount 2026.10.0 (x64)  
**Analysis Date:** 2026-10-25  

---

## 1. Confirmed Architectural Facts (High Confidence - Direct Evidence)

1. **Packaging & Runtimes:**
   - Both `RaiDrive.Mount.Service.x64.exe` and `RaiDrive.Mount.exe` are .NET Core / .NET 10.0 Single-File hosts embedding managed PE assemblies.
   - Evidence: `inspect_managed_artifact` SHA-256 digests, PE headers with `e_lfanew = 0x80`, ECMA-335 CLI metadata streams (`BSJB`, `#Strings`, `#~`).
2. **WinFsp Driver Integration:**
   - Directly calls native C exports of `winfsp-x64.dll` using `LoadLibraryW` and `GetProcAddress`.
   - Evidence: Extracted type `Fsp.Interop.Api` and `Fsp.Interop.FileSystemInterface` matching exact WinFsp C structure layouts and callback function pointers.
3. **Session & UAC Bridging:**
   - Spawns `NotifyHelper.exe` twice per mount (`scope=User` and `scope=Admin`) to map the drive letter in both logon session LUIDs using `DefineDosDeviceW`.
   - Evidence: Direct log traces in `service.log` with session IDs and exit codes.
4. **Configuration & Encryption:**
   - Drive definitions stored in Registry: `HKCU\Software\OpenBoxLab\RaiDrive Mount\Drives\<GUID>`.
   - Payload encrypted with Windows DPAPI (`0x01 0x00 0x00 0x00` DPAPI blob header).
   - Evidence: Registry readback and byte header inspection.

---

## 2. Inferred Behaviors (Medium Confidence)

1. **SQLite Database Schema:**
   - `Microsoft.Data.Sqlite.SqliteConnection` is referenced in the service assembly, and the service log records `Database Init Start / Done`.
   - Exact schema: Inferred to store directory node hierarchies, file revision timestamps, and cache eviction records. Not directly observed as a persistent `.db` file on disk during our snapshot (likely kept in-memory or dynamically created during file streaming).
2. **Internal Proprietary Obfuscation:**
   - Non-public classes in `RaiDrive.Mount.Service.x64.dll` are renamed with synthetic 36-character alphabetic identifiers (`GAIMLFHFHKIMNDDFLMAKPAGIDHNOJIKGIIGI`).
   - Clean-room development is preferred and necessary to maintain independent copyright and clean engineering.

---

## 3. Residual Unknowns & Bounded Limitations (To Not Speculate)

1. **Proprietary License Server Protocol:**
   - SignalR threads connect to RaiDrive license backend (`LisenseSession : Create SignalR Thread`).
   - *Resolution:* Out of scope. OpenMount is an open, independent tool and does not connect to or implement proprietary commercial licensing servers.
2. **Bandwidth Throttling Micro-Algorithms:**
   - Exact token-bucket parameters for bandwidth limiting (`CANBANDWIDTH False` in log).
   - *Resolution:* Standard token-bucket or leaky-bucket rate limiting can be implemented in the storage provider stream wrapper if requested.
3. **Shell Extension Context Menu IPC:**
   - `RaiDrive.ShellExtension.x64.dll` interacts with Windows Explorer via COM interfaces (`IShellExtInit`, `IContextMenu`).
   - *Resolution:* Non-critical for MVP. Drive letter mounting functions completely via WinFsp and Windows shell notifications without requiring a custom shell extension DLL.
