# Unknowns and Analysis Limitations Ledger

**Target:** RaiDrive Mount 2026.10.0 (x64) vs. OpenMount Clean-Room Implementation  
**Analysis & Verification Date:** 2026-10-25  
**Engineer:** Byte (Senior Systems & Filesystem Engineer)

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

## 2. Google Drive Live Testing & Setup Guidance

Due to absence of pre-configured live client credentials in the testing environment, Google Drive operations are validated via comprehensive mock-based tests (`GoogleDriveTests.cs` and `ResumableUploadAndRecoveryTests.cs`, 5 passing tests covering OAuth, PKCE, metadata mapping, duplicate name disambiguation, range streaming, resumable upload chunking, and staging preservation).

To execute live end-to-end mounts against a production Google Drive account:

1. **Google Cloud Console Project**:
   - Access [Google Cloud Console](https://console.cloud.google.com/).
   - Create a project: e.g., `OpenMount-Research`.
   - Enable the **Google Drive API** under `APIs & Services > Library`.
2. **OAuth Consent Screen**:
   - User Type: `External`.
   - Add scope: `https://www.googleapis.com/auth/drive`.
   - Add your Google account email under **Test Users**.
3. **OAuth Client ID**:
   - Application Type: `Desktop app`.
   - Obtain `Client ID` (e.g. `*.apps.googleusercontent.com`) and `Client Secret`.
4. **Mounting**:
   - In `OpenMount.Windows.exe`, click `+ Add Drive` -> Select `Google Drive`.
   - Enter `Client ID` and `Client Secret` -> Click `Authorize with Google...`.
   - Consent in the opened browser window. Tokens are encrypted automatically to `%APPDATA%\OpenMount\tokens\user_primary.tok.enc`.
   - Select an available drive letter (e.g. `G:`) and click `Connect`.

---

## 3. Residual Unknowns & Bounded Limitations (Non-Speculative)

1. **Proprietary License Server Protocol:**
   - SignalR threads connect to RaiDrive license backend (`LisenseSession : Create SignalR Thread`).
   - *Status:* Out of scope. OpenMount is an open, independent tool and does not connect to or implement proprietary commercial licensing servers.
2. **Bandwidth Throttling Micro-Algorithms:**
   - Exact token-bucket parameters for bandwidth limiting (`CANBANDWIDTH False` in log).
   - *Status:* Standard token-bucket or leaky-bucket rate limiting can be integrated in the storage provider stream wrapper if required.
3. **Shell Extension Context Menu IPC:**
   - `RaiDrive.ShellExtension.x64.dll` interacts with Windows Explorer via COM interfaces (`IShellExtInit`, `IContextMenu`).
   - *Status:* Non-critical. Native Windows drive mounting functions completely via WinFsp and Windows shell notifications (`SHChangeNotify`) without requiring custom Explorer COM shell extensions.
