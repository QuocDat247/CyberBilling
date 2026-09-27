using CyberBilling.Shared.Networking;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace CyberBilling.Client.Networking;

public sealed class TcpBillingClient :
    IAsyncDisposable
{
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

                    string? response =
                        await ReadLineWithTimeoutAsync(
                            reader,
                            HeartbeatResponseTimeout,
                            cancellationToken);

                    if (response is null)
                    {
                        throw new IOException(
                            "Server đã đóng kết nối.");
                    }

                    ProtocolMessage?
                        responseMessage =
                            ProtocolJson
                                .DeserializeMessage(
                                    response);

                    if (responseMessage is null
                        || !string.Equals(
                            responseMessage.Type,
                            MessageTypes
                                .HeartbeatAck,
                            StringComparison.Ordinal))
                    {
                        throw new IOException(
                            "Heartbeat response "
                            + "không hợp lệ.");
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
                        var shutdown =
                            new ClientShutdownPayload(
                                machineId,
                                DateTime.UtcNow);

                        string shutdownMessage =
                            ProtocolJson.Serialize(
                                MessageTypes
                                    .ClientShutdown,
                                shutdown);

                        await writer
                            .WriteLineAsync(
                                shutdownMessage);
                    }
                    catch
                    {
                        // Máy đang đóng nên không
                        // để lỗi gửi shutdown cản lại.
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
}