# OpenMount Implementation & Codebase Baseline Audit

**Date**: 2026-10-25  
**Auditor**: Byte (Senior Systems & Filesystem Engineer)  
**Target**: `D:\AI\MOUT`

---

## 1. Executive Summary

A comprehensive architectural and functional baseline audit was conducted on the existing OpenMount codebase prior to implementing the Google Drive milestone and Windows Desktop UI.

Baseline test execution (`dotnet test OpenMount.sln -c Release`):
- **16 passed, 0 failed, 0 skipped** across 4 test classes (`LocalStorageProviderTests`, `DpapiConfigStoreTests`, `VirtualFileSystemSemanticsTests`, `S3StorageProviderTests`, `MountIntegrationTests`).

However, static analysis and security auditing uncovered several critical design defects and missing components that must be stabilized before production readiness.

---

## 2. Component-by-Component Findings Matrix

| Component | Status | Finding & Evidence |
| :--- | :--- | :--- |
| **Security Descriptor (`VirtualFileSystem.cs`)** | **Verified defective** | Uses SDDL `O:BAG:BAD:(A;;FA;;;WD)` granting World (Everyone) full control over the mounted volume. This violates the least-privilege principle and allows non-elevated cross-session users to manipulate files. Must be constrained to Current User, Local System, and Builtin Administrators. |
| **Active FileNode Management (`VirtualFileSystem.cs`)** | **Verified defective** | Every `Open` and `Create` call instantiates an isolated `VirtualFileNode` and `VirtualFileDesc`. Two concurrent handles opening the same path do not share the same node or lock object, leading to disjoint staging files and potential data clobbering. |
| **Local Staging Buffer Lifecycle (`VirtualFileSystem.cs`)** | **Implemented but insufficiently tested** | File writes are buffered to `%TEMP%\OpenMount\staging\<guid>.tmp` and committed during `Cleanup`. If an unhandled exception or crash occurs before `Cleanup`, temporary files may leak without recovery or rollback mechanisms. |
| **Path Normalization & Traversal (`VirtualFileSystem.cs`)** | **Verified working** | Paths are normalized to leading backslashes (`\folder\file.txt`). Windows path delimiters handled consistently. |
| **Directory Enumeration (`VirtualFileSystem.cs`)** | **Verified working** | Synthesizes mandatory `.` and `..` entries and supports marker-based pagination for large directories. |
| **Local Storage Provider (`LocalStorageProvider.cs`)** | **Verified working** | Fully verified with automated unit tests for creation, range/sliced streaming, directory iteration, moving, and deletion. |
| **S3 Storage Provider (`S3StorageProvider.cs`)** | **Implemented but insufficiently tested** | HTTP byte-range requests and S3 hierarchy mapping implemented and verified via unit tests with mocked `IAmazonS3`. Real live network resilience under packet loss or throttling remains untested due to absence of live AWS credentials. |
| **Google Drive Provider (`GoogleDriveStorageProvider`)** | **Not implemented** | No provider or OAuth logic currently exists for Google Drive. Required as the primary deliverable for Stage C. |
| **DPAPI Configuration Store (`DpapiConfigStore.cs`)** | **Verified working** | Encrypts drive configs using `ProtectedData.Protect` under `DataProtectionScope.CurrentUser`. Verified via automated tests that ciphertext does not leak plaintext keys. Needs corrupted-data resilience fallback. |
| **Drive Manager & Device Helper (`DriveManager.cs`)** | **Verified working** | Correctly coordinates `Fsp.FileSystemHost` lifecycle, checks drive availability via `QueryDosDeviceW`, and notifies the Windows Shell via `SHChangeNotify`. End-to-end integration test succeeded on a real drive letter. |
| **Windows Desktop UI (`OpenMount.Windows`)** | **Not implemented** | The application currently operates strictly as a CLI runner (`OpenMount.Cli`). A lightweight native Windows dashboard with system tray integration is required. |

---

## 3. Detailed Architectural Defects & Remediation Plan

### Defect 1: Excessive Permissions in Security Descriptor
- **Vulnerability**: `O:BAG:BAD:(A;;FA;;;WD)` gives universal access.
- **Remediation**: Dynamically resolve the caller's Windows SID (`WindowsIdentity.GetCurrent().User.Value`) and build an SDDL granting Full Control strictly to `SY` (Local System), `BA` (Builtin Administrators), and `UserSid`.

### Defect 2: Missing Node Registry (Disjoint File Nodes)
- **Problem**: When a file is opened by two different handles (e.g. reader + writer), two independent `VirtualFileNode` objects are allocated. Locks do not synchronize.
- **Remediation**: Implement a thread-safe active node registry (`ConcurrentDictionary<string, VirtualFileNode>`). Increment reference counts on `Open`/`Create`, share the node across handles, and decrement on `Close`. Prune the node from the table only when open count reaches zero.

### Defect 3: Google Drive API & OAuth Architecture Requirement
- **Requirement**: Native OAuth 2.0 PKCE flow on loopback interface (`http://127.0.0.1:<port>/`), zero hardcoded secrets, DPAPI-protected token store, Google Workspace document export policy (PDF/DOCX/XLSX), file ID-based tracking to handle duplicate names in Drive folders.

---

## 4. Verification Checkpoint

A Git checkpoint will be maintained before performing modifications to `VirtualFileSystem` and adding the new Google Drive and UI projects.
