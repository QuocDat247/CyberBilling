using System.Collections.Concurrent;
using CyberBilling.Client.SystemControl;
using CyberBilling.Shared.Networking;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace CyberBilling.Client.Networking;

public sealed class TcpBillingClient :
    IAsyncDisposable
{
    private readonly ConcurrentQueue<
        AdminLoginRequestPayload>
        _adminLoginRequests =
            new();

    private const int
        TcpBillingServerPort =
            5055;

    private static readonly TimeSpan
        HeartbeatInterval =
            TimeSpan.FromSeconds(5);

    private static readonly TimeSpan
        HeartbeatResponseTimeout =
            TimeSpan.FromSeconds(8);

    private static readonly TimeSpan
        ReconnectDelay =
            TimeSpan.FromSeconds(3);

    private CancellationTokenSource?
        _runCancellation;

    private Task?
        _runTask;

    public event EventHandler<
        ClientConnectionStateChangedEventArgs>?
        ConnectionStateChanged;

    public event Action<
        WorkstationCommandType>?
        WorkstationCommandReceived;

    public event Action<
        AdminLoginResultPayload>?
        AdminLoginResultReceived;

    public bool IsRunning =>
        _runTask is not null;

    public Task StartAsync(
        string serverAddress,
        string machineName)
    {
        if (_runTask is not null)
        {
            return Task.CompletedTask;
        }

        _runCancellation =
            new CancellationTokenSource();

        _runTask =
            RunAsync(
                serverAddress,
                machineName,
                _runCancellation.Token);

        return Task.CompletedTask;
    }

    private async Task RunAsync(
        string serverAddress,
        string machineName,
        CancellationToken cancellationToken)
    {
        string machineId =
            ClientIdentity
                .GetOrCreateMachineId();

        bool firstAttempt =
            true;

        while (!cancellationToken
                   .IsCancellationRequested)
        {
            TcpClient? client =
                null;

            StreamReader? reader =
                null;

            StreamWriter? writer =
                null;

            bool connected =
                false;

            try
            {
                RaiseStateChanged(
                    firstAttempt
                        ? ClientConnectionState
                            .Connecting
                        : ClientConnectionState
                            .Reconnecting,
                    firstAttempt
                        ? "Đang kết nối..."
                        : "Đang kết nối lại...");

                client =
                    new TcpClient();

                await client
                    .ConnectAsync(
                        serverAddress,
                        TcpBillingServerPort,
                        cancellationToken);

                NetworkStream stream =
                    client.GetStream();

                reader =
                    new StreamReader(
                        stream,
                        Encoding.UTF8,
                        detectEncodingFromByteOrderMarks:
                            false,
                        leaveOpen:
                            true);

                writer =
                    new StreamWriter(
                        stream,
                        new UTF8Encoding(
                            encoderShouldEmitUTF8Identifier:
                                false),
                        leaveOpen:
                            true)
                    {
                        AutoFlush = true
                    };

                var hello =
                    new ClientHelloPayload(
                        machineId,
                        machineName);

                string helloMessage =
                    ProtocolJson.Serialize(
                        MessageTypes.ClientHello,
                        hello);

                await writer
                    .WriteLineAsync(
                        helloMessage);

                connected =
                    true;

                firstAttempt =
                    false;

                RaiseStateChanged(
                    ClientConnectionState
                        .Connected,
                    $"Đã kết nối tới "
                    + $"{serverAddress}:"
                    + TcpBillingServerPort);

                while (!cancellationToken
                           .IsCancellationRequested)
                {
                    while (_adminLoginRequests
                       .TryDequeue(
                           out AdminLoginRequestPayload?
                               loginRequest))
                    {
                        string loginMessage =
                            ProtocolJson.Serialize(
                                MessageTypes.AdminLoginRequest,
                                loginRequest);

                        await writer.WriteLineAsync(
                            loginMessage);
                    }

                    var heartbeat =
                        new HeartbeatPayload(
                            machineId,
                            DateTime.UtcNow);

                    string heartbeatMessage =
                        ProtocolJson.Serialize(
                            MessageTypes.Heartbeat,
                            heartbeat);

                    await writer
                        .WriteLineAsync(
                            heartbeatMessage);

                    bool heartbeatAcknowledged =
    false;

                    while (!heartbeatAcknowledged)
                    {
                        string? response =
                            await ReadLineWithTimeoutAsync(
                                reader,
                                HeartbeatResponseTimeout,
                                cancellationToken);

                        if (response is null)
                        {
                            throw new IOException(
                                "Máy tính tiền đã đóng kết nối.");
                        }

                        ProtocolMessage?
                            responseMessage =
                                ProtocolJson
                                    .DeserializeMessage(
                                        response);

                        if (responseMessage is null)
                        {
                            continue;
                        }

                        if (string.Equals(
                                responseMessage.Type,
                                MessageTypes.HeartbeatAck,
                                StringComparison.Ordinal))
                        {
                            heartbeatAcknowledged =
                                true;

                            continue;
                        }

                        if (string.Equals(
                                responseMessage.Type,
                                MessageTypes.AdminLoginResult,
                                StringComparison.Ordinal))
                        {
                            AdminLoginResultPayload?
                                loginResult =
                                    ProtocolJson
                                        .DeserializePayload<
                                            AdminLoginResultPayload>(
                                                responseMessage);

                            if (loginResult is not null)
                            {
                                AdminLoginResultReceived?
                                    .Invoke(
                                        loginResult);
                            }

                            continue;
                        }

                        if (string.Equals(
                                responseMessage.Type,
                                MessageTypes.WorkstationCommand,
                                StringComparison.Ordinal))
                        {
                            WorkstationCommandPayload?
                                command =
                                    ProtocolJson
                                        .DeserializePayload<
                                            WorkstationCommandPayload>(
                                                responseMessage);

                            if (command is null)
                            {
                                continue;
                            }

                            if (command.Command ==
                                    WorkstationCommandType.LockScreen
                                || command.Command ==
                                    WorkstationCommandType.UnlockScreen)
                            {
                                WorkstationCommandReceived?
                                    .Invoke(
                                        command.Command);

                                continue;
                            }

                            bool powerCommand =
                                command.Command ==
                                    WorkstationCommandType
                                        .Restart
                                || command.Command ==
                                    WorkstationCommandType
                                        .Shutdown;

                            if (powerCommand)
                            {
                                /*
                                 * Chỉ gửi trạng thái "Đã tắt"
                                 * sau khi Windows chấp nhận
                                 * lệnh restart/shutdown.
                                 */
                                WorkstationCommandExecutor
                                    .Execute(
                                        command.Command);

                                await SendShutdownNoticeAsync(
                                    writer,
                                    machineId);

                                return;
                            }

                            WorkstationCommandExecutor
                                .Execute(
                                    command.Command);

                            continue;
                        }

                        throw new IOException(
                            "Phản hồi từ máy tính tiền không hợp lệ.");
                    }

                    await Task.Delay(
                        HeartbeatInterval,
                        cancellationToken);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken
                    .IsCancellationRequested)
            {
                // Client đang đóng bình thường.
            }
            catch (Exception)
            {
                if (!cancellationToken
                    .IsCancellationRequested)
                {
                    RaiseStateChanged(
                        ClientConnectionState
                            .Reconnecting,
                        "Mất kết nối - "
                        + "đang thử lại...");
                }
            }
            finally
            {
                if (connected
                    && cancellationToken
                        .IsCancellationRequested
                    && writer is not null)
                {
                    try
                    {
                        await SendShutdownNoticeAsync(
                            writer,
                            machineId);
                    }
                    catch
                    {
                        /*
                         * Máy đang đóng nên không để lỗi
                         * thông báo shutdown cản lại.
                         */
                    }
                }

                writer?.Dispose();
                reader?.Dispose();
                client?.Dispose();
            }

            if (cancellationToken
                .IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(
                    ReconnectDelay,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        RaiseStateChanged(
            ClientConnectionState.Stopped,
            "Đã dừng kết nối.");
    }

    private static async Task<string?>
        ReadLineWithTimeoutAsync(
            StreamReader reader,
            TimeSpan timeout,
            CancellationToken cancellationToken)
    {
        using var timeoutCancellation =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);

        timeoutCancellation
            .CancelAfter(
                timeout);

        try
        {
            return await reader
                .ReadLineAsync(
                    timeoutCancellation.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken
                .IsCancellationRequested)
        {
            throw new IOException(
                "Heartbeat timeout.");
        }
    }

    private static async Task
        SendShutdownNoticeAsync(
            StreamWriter writer,
            string machineId)
    {
        var shutdown =
            new ClientShutdownPayload(
                machineId,
                DateTime.UtcNow);

        string shutdownMessage =
            ProtocolJson.Serialize(
                MessageTypes.ClientShutdown,
                shutdown);

        await writer
            .WriteLineAsync(
                shutdownMessage);
    }

    private void RaiseStateChanged(
        ClientConnectionState state,
        string message)
    {
        ConnectionStateChanged?
            .Invoke(
                this,
                new ClientConnectionStateChangedEventArgs(
                    state,
                    message));
    }

    public async Task StopAsync()
    {
        if (_runTask is null)
        {
            return;
        }

        _runCancellation?
            .Cancel();

        try
        {
            await _runTask;
        }
        catch (OperationCanceledException)
        {
        }

        _runCancellation?
            .Dispose();

        _runCancellation =
            null;

        _runTask =
            null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    public void RequestAdminLogin(
        string username,
        string password)
    {
        _adminLoginRequests.Enqueue(
            new AdminLoginRequestPayload(
                username,
                password));
    }
}