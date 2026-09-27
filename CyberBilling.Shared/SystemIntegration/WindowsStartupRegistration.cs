using Microsoft.Win32;
using System.IO;

namespace CyberBilling.Shared.SystemIntegration;

public static class WindowsStartupRegistration
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool TryRegisterCurrentExecutable(
        string startupName,
        string expectedExecutableFileName)
    {
        try
        {
            string? processPath =
                Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(
                    processPath))
            {
                return false;
            }

            /*
             * Khi phát triển ta đang chạy:
             *
             * dotnet CyberBilling.Client.dll
             *
             * Environment.ProcessPath lúc đó
             * chính là dotnet.exe.
             *
             * Tuyệt đối không đăng ký dotnet.exe
             * vào Startup.
             */
            if (!string.Equals(
                    Path.GetFileName(
                        processPath),
                    expectedExecutableFileName,
                    StringComparison
                        .OrdinalIgnoreCase))
            {
                return false;
            }

            using RegistryKey? key =
                Registry.CurrentUser
                    .CreateSubKey(
                        RunKeyPath);

            if (key is null)
            {
                return false;
            }

            key.SetValue(
                startupName,
                $"\"{processPath}\"",
                RegistryValueKind.String);

            return true;
        }
        catch
        {
            /*
             * Startup thất bại không được làm
             * CyberBilling ngừng hoạt động.
             */
            return false;
        }
    }
}