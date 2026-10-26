using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace OpenMount.Windows
{
    public static class ThemeManager
    {
        private const string RegistryKey = @"Software\OpenMount";
        private const string ThemeValueName = "DarkMode";

        public static bool IsDarkMode { get; private set; }

        public static void Initialize()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
                if (key?.GetValue(ThemeValueName) is int val)
                {
                    IsDarkMode = val == 1;
                }
                else
                {
                    // Detect Windows system dark mode
                    IsDarkMode = DetectSystemDarkMode();
                }
            }
            catch
            {
                IsDarkMode = false;
            }

            ApplyTheme(IsDarkMode);
        }

        public static void ToggleTheme()
        {
            SetDarkMode(!IsDarkMode);
        }

        public static void SetDarkMode(bool dark)
        {
            IsDarkMode = dark;
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKey);
                key?.SetValue(ThemeValueName, dark ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }

            ApplyTheme(dark);
        }

        public static void ApplyTheme(bool dark)
        {
            var res = System.Windows.Application.Current?.Resources;
            if (res == null) return;

            if (dark)
            {
                // Dark Palette
                SetBrush(res, "BrushWindowBg", "#18191A");
                SetBrush(res, "BrushHeaderBg", "#242526");
                SetBrush(res, "BrushCardBg", "#242526");
                SetBrush(res, "BrushCardHover", "#2D2E30");
                SetBrush(res, "BrushCardBorder", "#3A3B3C");
                SetBrush(res, "BrushSubtleBorder", "#3A3B3C");
                SetBrush(res, "BrushTextPrimary", "#F5F5F7");
                SetBrush(res, "BrushTextSecondary", "#A0A2A8");
                SetBrush(res, "BrushTextMuted", "#72767D");
                SetBrush(res, "BrushDriveBadgeBg", "#1E3A5F");
                SetBrush(res, "BrushDriveBadgeFg", "#60A5FA");
                SetBrush(res, "BrushTagBg", "#3A3B3C");
                SetBrush(res, "BrushTagFg", "#E4E6EB");
                SetBrush(res, "BrushProgressTrack", "#3A3B3C");
                SetBrush(res, "BrushInputBg", "#18191A");
                SetBrush(res, "BrushInputBorder", "#3A3B3C");
                SetBrush(res, "BrushInputFg", "#F5F5F7");
                SetBrush(res, "BrushSecondaryBtnBg", "#3A3B3C");
                SetBrush(res, "BrushSecondaryBtnFg", "#E4E6EB");
                SetBrush(res, "BrushSecondaryBtnHover", "#4E4F50");
                SetBrush(res, "BrushDangerBtnSubtleBg", "#3D1A1E");
                SetBrush(res, "BrushDangerBtnSubtleFg", "#F87171");
                SetBrush(res, "BrushDangerBtnSubtleHover", "#522328");
                SetBrush(res, "BrushStatusBarBg", "#202124");
                SetBrush(res, "BrushThemeToggleBg", "#3A3B3C");
                SetBrush(res, "BrushThemeToggleFg", "#FCD34D"); // Warm yellow sun/star
            }
            else
            {
                // Light Palette
                SetBrush(res, "BrushWindowBg", "#F4F6F9");
                SetBrush(res, "BrushHeaderBg", "#FFFFFF");
                SetBrush(res, "BrushCardBg", "#FFFFFF");
                SetBrush(res, "BrushCardHover", "#F8F9FA");
                SetBrush(res, "BrushCardBorder", "#E4E6EB");
                SetBrush(res, "BrushSubtleBorder", "#E9ECEF");
                SetBrush(res, "BrushTextPrimary", "#212529");
                SetBrush(res, "BrushTextSecondary", "#6C757D");
                SetBrush(res, "BrushTextMuted", "#868E96");
                SetBrush(res, "BrushDriveBadgeBg", "#E7F1FF");
                SetBrush(res, "BrushDriveBadgeFg", "#0078D4");
                SetBrush(res, "BrushTagBg", "#E9ECEF");
                SetBrush(res, "BrushTagFg", "#495057");
                SetBrush(res, "BrushProgressTrack", "#E9ECEF");
                SetBrush(res, "BrushInputBg", "#FFFFFF");
                SetBrush(res, "BrushInputBorder", "#CED4DA");
                SetBrush(res, "BrushInputFg", "#212529");
                SetBrush(res, "BrushSecondaryBtnBg", "#E9ECEF");
                SetBrush(res, "BrushSecondaryBtnFg", "#333333");
                SetBrush(res, "BrushSecondaryBtnHover", "#DEE2E6");
                SetBrush(res, "BrushDangerBtnSubtleBg", "#FEE2E2");
                SetBrush(res, "BrushDangerBtnSubtleFg", "#DC3545");
                SetBrush(res, "BrushDangerBtnSubtleHover", "#FCD0D0");
                SetBrush(res, "BrushStatusBarBg", "#FFFFFF");
                SetBrush(res, "BrushThemeToggleBg", "#E9ECEF");
                SetBrush(res, "BrushThemeToggleFg", "#4B5563");
            }
        }

        private static void SetBrush(ResourceDictionary res, string key, string hex)
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            res[key] = brush;
        }

        private static bool DetectSystemDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("AppsUseLightTheme") is int val)
                {
                    return val == 0; // 0 = dark mode, 1 = light mode
                }
            }
            catch { }
            return false;
        }
    }
}
