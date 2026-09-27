using CyberBilling.Shared.Networking;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CyberBilling.Server.Networking;

public sealed class TcpBillingServer :
    IAsyncDisposable
{
    public const int DefaultPort =
        5055;

    private static readonly TimeSpan
        ClientTimeout =
            TimeSpan.FromSeconds(15);

    private TcpListener?
        _listener;

    private CancellationTokenSource?
        _cancellationTokenSource;

    private Task?
        _acceptLoopTask;

    public event EventHandler<
        WorkstationConnectionInfo>?
        WorkstationConnectionChanged;

    public bool IsRunning =>
        _listener is not null;

    public Task StartAsync(
        CancellationToken cancellationToken =
            default)
    {
        if (_listener is not null)
        {
            return Task.CompletedTask;
        }

        _cancellationTokenSource =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);

        _listener =
            new TcpListener(
                IPAddress.Any,
                DefaultPort);

        _listener.Start();

        _acceptLoopTask =
            AcceptLoopAsync(
                _cancellationTokenSource.Token);

        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(
        CancellationToken cancellationToken)
    {
        while (!cancellationToken
                   .IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client =
                    await _listener!
                        .AcceptTcpClientAsync(
                            cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ =
                HandleClientAsync(
                    client,
                    cancellationToken);
        }
    }

    private async Task HandleClientAsync(
        TcpClient client,
        CancellationToken cancellationToken)
    {
        WorkstationConnectionInfo?
            connectionInfo = null;

        bool gracefulShutdownReceived =
            false;

        try
        {
            using NetworkStream stream =
                client.GetStream();

            using var reader =
                new StreamReader(
                    stream,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks:
                        false,
                    leaveOpen:
                        true);

            using var writer =
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

            string? firstLine =
                await ReadLineWithTimeoutAsync(
                    reader,
                    ClientTimeout,
                    cancellationToken);

            if (string.IsNullOrWhiteSpace(
                    firstLine))
            {
                return;
            }

            ProtocolMessage? helloMessage =
                ProtocolJson
                    .DeserializeMessage(
                        firstLine);

            if (helloMessage is null
                || !string.Equals(
                    helloMessage.Type,
                    MessageTypes.ClientHello,
                    StringComparison.Ordinal))
            {
                return;
            }

            ClientHelloPayload? hello =
                ProtocolJson
                    .DeserializePayload<
                        ClientHelloPayload>(
                            helloMessage);

            if (hello is null
                || string.IsNullOrWhiteSpace(
                    hello.MachineId)
                || string.IsNullOrWhiteSpace(
                    hello.MachineName))
            {
                return;
            }

            string remoteIp =
                ((IPEndPoint?)
                    client.Client.RemoteEndPoint)?
                    .Address
                    .ToString()
                ?? "Unknown";

            DateTime connectedAt =
                DateTime.Now;

            connectionInfo =
                new WorkstationConnectionInfo(
                    hello.MachineId,
                    hello.MachineName,
                    remoteIp,
                    WorkstationConnectionState
                        .Online,
                    connectedAt,
                    connectedAt);

            RaiseConnectionChanged(
                connectionInfo);

            while (!cancellationToken
                       .IsCancellationRequested)
            {
                string? line;

                try
                {
                    line =
                        await ReadLineWithTimeoutAsync(
                            reader,
                            ClientTimeout,
                            cancellationToken);
                }
                catch (TimeoutException)
                {
                    break;
                }

                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(
                        line))
                {
                    continue;
                }

                ProtocolMessage? message =
                    ProtocolJson
                        .DeserializeMessage(
                            line);

                if (message is null)
                {
                    continue;
                }

                if (string.Equals(
                        message.Type,
                        MessageTypes.Heartbeat,
                        StringComparison.Ordinal))
                {
                    HeartbeatPayload? heartbeat =
                        ProtocolJson
                            .DeserializePayload<
                                HeartbeatPayload>(
                                    message);

                    if (heartbeat is null)
                    {
                        continue;
                    }

                    DateTime now =
                        DateTime.Now;

                    connectionInfo =
                        connectionInfo with
                        {
                            State =
                                WorkstationConnectionState
                                    .Online,

                            LastSeen =
                                now
                        };

                    RaiseConnectionChanged(
                        connectionInfo);

                    string ack =
                        ProtocolJson.Serialize(
                            MessageTypes
                                .HeartbeatAck,
                            new HeartbeatAckPayload(
                                DateTime.UtcNow));

                    await writer
                        .WriteLineAsync(
                            ack);

                    continue;
                }

                if (string.Equals(
                        message.Type,
                        MessageTypes.ClientShutdown,
                        StringComparison.Ordinal))
                {
                    gracefulShutdownReceived =
                        true;

                    connectionInfo =
                        connectionInfo with
                        {
                            LastSeen =
                                DateTime.Now
                        };

                    break;
                }
            }
        }
        catch (TimeoutException)
        {
            // Không nhận được dữ liệu trong
            // khoảng timeout quy định.
        }
        catch (OperationCanceledException)
            when (cancellationToken
                .IsCancellationRequested)
        {
            // Server đang dừng.
        }
        catch (IOException)
        {
            // Kết nối bị mất.
        }
        catch (SocketException)
        {
            // Kết nối bị mất.
        }
        finally
        {
            client.Dispose();

            if (connectionInfo is not null
                && !cancellationToken
                    .IsCancellationRequested)
            {
                connectionInfo =
                    connectionInfo with
                    {
                        State =
                            gracefulShutdownReceived
                                ? WorkstationConnectionState
                                    .GracefulShutdown
                                : WorkstationConnectionState
                                    .ConnectionLost,

                        LastSeen =
                            DateTime.Now
                    };

                RaiseConnectionChanged(
                    connectionInfo);
            }
        }
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
            throw new TimeoutException();
        }
    }

    private void RaiseConnectionChanged(
        WorkstationConnectionInfo info)
    {
        WorkstationConnectionChanged?
            .Invoke(
                this,
                info);
    }

    public async ValueTask DisposeAsync()
    {
        if (_listener is null)
        {
            return;
        }

        _cancellationTokenSource?
            .Cancel();

        _listener.Stop();

        if (_acceptLoopTask is not null)
        {
            try
            {
                await _acceptLoopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cancellationTokenSource?
            .Dispose();

        _cancellationTokenSource =
            null;

        _listener =
            null;

        _acceptLoopTask =
            null;
    }
}