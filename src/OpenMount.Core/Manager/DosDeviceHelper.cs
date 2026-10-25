using System;
using System.Runtime.InteropServices;

namespace OpenMount.Core.Manager
{
    public static class DosDeviceHelper
    {
        public const uint DDD_RAW_TARGET_PATH = 0x00000001;
        public const uint DDD_REMOVE_DEFINITION = 0x00000002;
        public const uint DDD_EXACT_MATCH_ON_REMOVE = 0x00000004;
        public const uint DDD_NO_BROADCAST_SYSTEM = 0x00000008;

        public const int SHCNE_DRIVEREMOVED = 0x00000080;
        public const int SHCNE_DRIVEADD = 0x00000100;
        public const uint SHCNF_PATHW = 0x0005;
        public const uint SHCNF_FLUSH = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool DefineDosDeviceW(uint dwFlags, string lpDeviceName, string? lpTargetPath);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint QueryDosDeviceW(string? lpDeviceName, [Out] char[] lpTargetPath, uint ucchMax);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern void SHChangeNotify(int wEventId, uint uFlags, string dwItem1, IntPtr dwItem2);

        public static bool IsDriveInUse(string driveLetter)
        {
            string clean = driveLetter.TrimEnd('\\');
            char[] buffer = new char[1024];
            uint res = QueryDosDeviceW(clean, buffer, (uint)buffer.Length);
            return res > 0;
        }

        public static void NotifyDriveAdded(string driveLetter)
        {
            string path = driveLetter.TrimEnd('\\') + "\\";
            try
            {
                SHChangeNotify(SHCNE_DRIVEADD, SHCNF_PATHW | SHCNF_FLUSH, path, IntPtr.Zero);
            }
            catch { }
        }

        public static void NotifyDriveRemoved(string driveLetter)
        {
            string path = driveLetter.TrimEnd('\\') + "\\";
            try
            {
                SHChangeNotify(SHCNE_DRIVEREMOVED, SHCNF_PATHW | SHCNF_FLUSH, path, IntPtr.Zero);
            }
            catch { }
        }
    }
}
