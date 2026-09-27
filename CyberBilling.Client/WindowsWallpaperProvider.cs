using Microsoft.Win32;
using System.IO;

namespace CyberBilling.Client;

public static class WindowsWallpaperProvider
{
    public static string?
        TryGetWallpaperPath()
    {
        try
        {
            using RegistryKey? key =
                Registry.CurrentUser
                    .OpenSubKey(
                        @"Control Panel\Desktop");

            string? wallpaper =
                key?
                    .GetValue(
                        "WallPaper")
                    as string;

            if (!string.IsNullOrWhiteSpace(
                    wallpaper)
                && File.Exists(
                    wallpaper))
            {
                return wallpaper;
            }
        }
        catch
        {
        }

        /*
         * Windows thường lưu wallpaper hiện tại
         * ở đây khi dùng Theme / slideshow.
         */
        string fallback =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder
                        .ApplicationData),
                "Microsoft",
                "Windows",
                "Themes",
                "TranscodedWallpaper");

        return File.Exists(
                fallback)
            ? fallback
            : null;
    }
}