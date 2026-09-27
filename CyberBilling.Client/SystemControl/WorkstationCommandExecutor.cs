using System.Diagnostics;
using CyberBilling.Shared.Networking;

namespace CyberBilling.Client.SystemControl;

public static class WorkstationCommandExecutor
{
    public static void Execute(
        WorkstationCommandType command)
    {
        switch (command)
        {
            case WorkstationCommandType
                .CloseApplications:

                CloseApplications();

                break;

            case WorkstationCommandType
                .Restart:

                StartShutdown(
                    "/r /t 1");

                break;

            case WorkstationCommandType
                .Shutdown:

                StartShutdown(
                    "/s /t 1");

                break;

            default:
                throw new InvalidOperationException(
                    "Lệnh máy trạm không hợp lệ.");
        }
    }

    private static void CloseApplications()
    {
        int currentProcessId =
            Environment.ProcessId;

        using Process currentProcess =
            Process.GetCurrentProcess();

        int currentSessionId =
            currentProcess.SessionId;

        foreach (Process process
                 in Process.GetProcesses())
        {
            try
            {
                if (process.Id ==
                    currentProcessId)
                {
                    continue;
                }

                if (process.SessionId !=
                    currentSessionId)
                {
                    continue;
                }

                /*
                 * Trong lúc phát triển,
                 * Server và Client có thể đang
                 * chạy bằng dotnet.exe cùng máy.
                 *
                 * Không đóng các tiến trình
                 * CyberBilling hoặc dotnet.
                 */
                if (process.ProcessName
                        .Equals(
                            "dotnet",
                            StringComparison
                                .OrdinalIgnoreCase)
                    || process.ProcessName
                        .StartsWith(
                            "CyberBilling",
                            StringComparison
                                .OrdinalIgnoreCase))
                {
                    continue;
                }

                if (process.MainWindowHandle ==
                    IntPtr.Zero)
                {
                    continue;
                }

                /*
                 * Chỉ yêu cầu ứng dụng đóng
                 * bình thường.
                 *
                 * Không force-kill tiến trình.
                 */
                process.CloseMainWindow();
            }
            catch
            {
                /*
                 * Một số tiến trình Windows
                 * không cho phép truy cập.
                 * Bỏ qua chúng.
                 */
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void StartShutdown(
        string arguments)
    {
        Process.Start(
            new ProcessStartInfo
            {
                FileName =
                    "shutdown.exe",

                Arguments =
                    arguments,

                UseShellExecute =
                    false,

                CreateNoWindow =
                    true
            });
    }
}