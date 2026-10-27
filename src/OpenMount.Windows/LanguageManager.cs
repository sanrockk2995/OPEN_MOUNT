using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.Win32;

namespace OpenMount.Windows
{
    public static class LanguageManager
    {
        private const string RegistryKey = @"Software\OpenMount";
        private const string LanguageValueName = "Language";

        public static string CurrentLanguage { get; private set; } = "vi"; // Default to Vietnamese

        public static event Action? LanguageChanged;

        private static readonly Dictionary<string, string> ViDictionary = new()
        {
            ["Loc_AppTitle"] = "OpenMount - Trình quản lý ổ đĩa đám mây ảo",
            ["Loc_AppSubtitle"] = "Ổ đĩa đám mây ảo hiệu năng cao cho Windows",
            ["Loc_RunOnStartup"] = "Khởi động cùng Windows",
            ["Loc_DarkMode"] = "🌙 Giao diện tối",
            ["Loc_LightMode"] = "☀ Giao diện sáng",
            ["Loc_LanguageName"] = "Tiếng Việt",
            ["Loc_LanguageToggle"] = "🇻🇳 Tiếng Việt",
            ["Loc_AddDrive"] = "+ Thêm ổ đĩa",
            ["Loc_MountAll"] = "Kết nối tất cả",
            ["Loc_UnmountAll"] = "Ngắt tất cả",
            ["Loc_WinFspMissing"] = "Cần cài đặt driver WinFsp để gắn ổ đĩa đám mây ảo. Nhấn để cài đặt.",
            ["Loc_InstallWinFsp"] = "Cài đặt WinFsp ngay",
            ["Loc_EmptyTitle"] = "Chưa có ổ đĩa nào",
            ["Loc_EmptyDesc"] = "Gắn thư mục cục bộ, Google Drive, hoặc S3/MinIO thành ổ đĩa Windows trực tiếp.",
            ["Loc_AddFirstDrive"] = "+ Thêm ổ đĩa đầu tiên",
            ["Loc_StatusLabel"] = "Trạng thái: ",
            ["Loc_StorageLabel"] = "Dung lượng: ",
            ["Loc_Connect"] = "Kết nối",
            ["Loc_Disconnect"] = "Ngắt kết nối",
            ["Loc_Open"] = "Mở ổ",
            ["Loc_Edit"] = "Sửa",
            ["Loc_Remove"] = "Xóa",
            ["Loc_StatusMounted"] = "Đã kết nối",
            ["Loc_StatusConnecting"] = "Đang kết nối...",
            ["Loc_StatusError"] = "Lỗi",
            ["Loc_StatusStopped"] = "Đã dừng",
            ["Loc_StatusReady"] = "Sẵn sàng",
            ["Loc_DrivesCount"] = "ổ đĩa",

            // Dialog
            ["Loc_DialogTitleAdd"] = "Thêm ổ đĩa ảo mới",
            ["Loc_DialogTitleEdit"] = "Chỉnh sửa ổ đĩa ảo",
            ["Loc_GeneralSettings"] = "Cài đặt chung",
            ["Loc_DriveLetter"] = "Ký tự ổ đĩa:",
            ["Loc_DriveLabel"] = "Tên nhãn ổ đĩa:",
            ["Loc_MountLocation"] = "Vị trí gắn ổ:",
            ["Loc_MountNetwork"] = "Ổ đĩa mạng (Network locations)",
            ["Loc_MountLocal"] = "Ổ đĩa cục bộ (Local disk)",
            ["Loc_AutoMount"] = "Tự động kết nối khi khởi động",
            ["Loc_StorageProvider"] = "Dịch vụ lưu trữ",
            ["Loc_AdvancedOptions"] = "Tùy chọn nâng cao (Chế độ RaiDrive)",
            ["Loc_FilePreview"] = "Xem trước tệp (File preview)",
            ["Loc_InstantProperties"] = "Lấy thuộc tính tệp tức thì (Get file properties instantly)",
            ["Loc_AsyncUpload"] = "Cập nhật thay đổi bất đồng bộ (Update storage changes asynchronously)",
            ["Loc_MaintainDateModified"] = "Giữ nguyên ngày sửa đổi \"Date modified\" của tệp và thư mục",
            ["Loc_ProcessMonitor"] = "Theo dõi tiến trình truy cập tệp",
            ["Loc_QuickRemoteOps"] = "Sao chép/Di chuyển/Xóa nhanh trên bộ nhớ từ xa",
            ["Loc_ConflictResolution"] = "Xử lý xung đột:",
            ["Loc_OverwriteTarget"] = "Ghi đè đích",
            ["Loc_RenameSource"] = "Đổi tên nguồn",
            ["Loc_Skip"] = "Bỏ qua",
            ["Loc_Save"] = "Lưu cấu hình",
            ["Loc_Cancel"] = "Hủy",
            ["Loc_Browse"] = "Duyệt...",
            ["Loc_LocalPath"] = "Đường dẫn thư mục cục bộ:",
            ["Loc_GDriveAuth"] = "Xác thực tài khoản Google Drive:",
            ["Loc_GDriveLoginBtn"] = "Đăng nhập với Google",
            ["Loc_GDriveLoggedIn"] = "Đã đăng nhập tài khoản Google",
            ["Loc_S3Endpoint"] = "Địa chỉ Endpoint URL:",
            ["Loc_S3Bucket"] = "Tên Bucket:",
            ["Loc_S3Region"] = "Vùng (Region):",
            ["Loc_S3AccessKey"] = "Access Key ID:",
            ["Loc_S3SecretKey"] = "Secret Access Key:",
            ["Loc_S3ForcePathStyle"] = "Sử dụng Path-Style (Dành cho MinIO):",
            ["Loc_WebDavUrl"] = "Địa chỉ WebDAV URL:",
            ["Loc_Username"] = "Tên đăng nhập:",
            ["Loc_Password"] = "Mật khẩu:",
            ["Loc_Host"] = "Địa chỉ máy chủ (Host):",
            ["Loc_Port"] = "Cổng kết nối (Port):",
            ["Loc_RemotePath"] = "Thư mục từ xa (Path):",
            ["Loc_PCloudRegion"] = "Khu vực máy chủ pCloud:"
        };

        private static readonly Dictionary<string, string> EnDictionary = new()
        {
            ["Loc_AppTitle"] = "OpenMount - Virtual Cloud Drive Manager",
            ["Loc_AppSubtitle"] = "High-Performance Virtual Cloud Storage for Windows",
            ["Loc_RunOnStartup"] = "Run on Windows Startup",
            ["Loc_DarkMode"] = "🌙 Dark Mode",
            ["Loc_LightMode"] = "☀ Light Mode",
            ["Loc_LanguageName"] = "English",
            ["Loc_LanguageToggle"] = "🇬🇧 English",
            ["Loc_AddDrive"] = "+ Add Drive",
            ["Loc_MountAll"] = "Mount All",
            ["Loc_UnmountAll"] = "Unmount All",
            ["Loc_WinFspMissing"] = "WinFsp kernel driver is required to mount virtual cloud drives. Click to install.",
            ["Loc_InstallWinFsp"] = "Install WinFsp Now",
            ["Loc_EmptyTitle"] = "No Drives Configured",
            ["Loc_EmptyDesc"] = "Mount your local folders, Google Drive, or S3/MinIO buckets as native Windows drive letters.",
            ["Loc_AddFirstDrive"] = "+ Add Your First Drive",
            ["Loc_StatusLabel"] = "Status: ",
            ["Loc_StorageLabel"] = "Storage: ",
            ["Loc_Connect"] = "Connect",
            ["Loc_Disconnect"] = "Disconnect",
            ["Loc_Open"] = "Open",
            ["Loc_Edit"] = "Edit",
            ["Loc_Remove"] = "Remove",
            ["Loc_StatusMounted"] = "Mounted",
            ["Loc_StatusConnecting"] = "Connecting...",
            ["Loc_StatusError"] = "Error",
            ["Loc_StatusStopped"] = "Stopped",
            ["Loc_StatusReady"] = "Ready",
            ["Loc_DrivesCount"] = "drives",

            // Dialog
            ["Loc_DialogTitleAdd"] = "Add Virtual Cloud Drive",
            ["Loc_DialogTitleEdit"] = "Edit Virtual Cloud Drive",
            ["Loc_GeneralSettings"] = "General Settings",
            ["Loc_DriveLetter"] = "Drive Letter:",
            ["Loc_DriveLabel"] = "Drive Label:",
            ["Loc_MountLocation"] = "Mount Location:",
            ["Loc_MountNetwork"] = "Network locations (Ổ đĩa mạng)",
            ["Loc_MountLocal"] = "Local disk (Cục bộ)",
            ["Loc_AutoMount"] = "Mount automatically on startup",
            ["Loc_StorageProvider"] = "Storage Provider",
            ["Loc_AdvancedOptions"] = "Advanced Options (RaiDrive Mode)",
            ["Loc_FilePreview"] = "File preview",
            ["Loc_InstantProperties"] = "Get file properties instantly",
            ["Loc_AsyncUpload"] = "Update storage changes asynchronously",
            ["Loc_MaintainDateModified"] = "Maintain \"Date modified\" of files and folders",
            ["Loc_ProcessMonitor"] = "Monitor process access to files",
            ["Loc_QuickRemoteOps"] = "Quick Copy/Move/Delete for remote storage",
            ["Loc_ConflictResolution"] = "Conflict Resolution:",
            ["Loc_OverwriteTarget"] = "Overwrite target",
            ["Loc_RenameSource"] = "Rename source",
            ["Loc_Skip"] = "Skip",
            ["Loc_Save"] = "Save Configuration",
            ["Loc_Cancel"] = "Cancel",
            ["Loc_Browse"] = "Browse...",
            ["Loc_LocalPath"] = "Local Directory Path:",
            ["Loc_GDriveAuth"] = "Google Drive Authentication:",
            ["Loc_GDriveLoginBtn"] = "Sign in with Google",
            ["Loc_GDriveLoggedIn"] = "Signed in with Google",
            ["Loc_S3Endpoint"] = "Endpoint URL:",
            ["Loc_S3Bucket"] = "Bucket Name:",
            ["Loc_S3Region"] = "Region:",
            ["Loc_S3AccessKey"] = "Access Key ID:",
            ["Loc_S3SecretKey"] = "Secret Access Key:",
            ["Loc_S3ForcePathStyle"] = "Use Path-Style (For MinIO):",
            ["Loc_WebDavUrl"] = "WebDAV URL:",
            ["Loc_Username"] = "Username:",
            ["Loc_Password"] = "Password:",
            ["Loc_Host"] = "Host:",
            ["Loc_Port"] = "Port:",
            ["Loc_RemotePath"] = "Remote Path:",
            ["Loc_PCloudRegion"] = "pCloud Server Region:"
        };

        public static void Initialize()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
                if (key?.GetValue(LanguageValueName) is string lang && !string.IsNullOrWhiteSpace(lang))
                {
                    CurrentLanguage = lang;
                }
                else
                {
                    // Default to Vietnamese ("vi")
                    CurrentLanguage = "vi";
                }
            }
            catch
            {
                CurrentLanguage = "vi";
            }

            ApplyLanguage(CurrentLanguage);
        }

        public static void ToggleLanguage()
        {
            string next = CurrentLanguage == "vi" ? "en" : "vi";
            SetLanguage(next);
        }

        public static void SetLanguage(string lang)
        {
            CurrentLanguage = lang == "en" ? "en" : "vi";
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKey);
                key?.SetValue(LanguageValueName, CurrentLanguage, RegistryValueKind.String);
            }
            catch { }

            ApplyLanguage(CurrentLanguage);
            LanguageChanged?.Invoke();
        }

        public static void ApplyLanguage(string lang)
        {
            var res = System.Windows.Application.Current?.Resources;
            if (res == null) return;

            var dict = lang == "en" ? EnDictionary : ViDictionary;
            foreach (var kvp in dict)
            {
                res[kvp.Key] = kvp.Value;
            }
        }

        public static string Get(string key)
        {
            var dict = CurrentLanguage == "en" ? EnDictionary : ViDictionary;
            if (dict.TryGetValue(key, out var val))
            {
                return val;
            }
            return key;
        }
    }
}
