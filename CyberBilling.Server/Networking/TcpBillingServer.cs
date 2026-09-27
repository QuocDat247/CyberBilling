using CyberBilling.Shared.Networking;
using System.Collections.Concurrent;
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

    private readonly object
        _commandChannelsLock =
            new();

    private readonly Dictionary<
        string,
        ClientCommandChannel>
        _commandChannels =
            new(
                StringComparer.Ordinal);

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

    public bool TryQueueWorkstationCommand(
        string machineId,
        WorkstationCommandType command)
    {
        lock (_commandChannelsLock)
        {
            if (!_commandChannels
                    .TryGetValue(
                        machineId,
                        out ClientCommandChannel?
                            channel))
            {
                return false;
            }

            channel.Commands.Enqueue(
                command);

            return true;
        }
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

        ClientCommandChannel?
            commandChannel = null;

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

            commandChannel =
                new ClientCommandChannel();

            lock (_commandChannelsLock)
            {
                _commandChannels[
                    hello.MachineId] =
                    commandChannel;
            }

            string remoteIp =
                ((IPEndPoint?)
                    client.Client.RemoteEndPoint)?
                    .Address
                    .ToString()
                ?? "Không xác định";

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

                    /*
                     * Gửi các lệnh đang chờ
                     * trước HeartbeatAck.
                     *
                     * Client sẽ xử lý từng lệnh,
                     * sau đó tiếp tục đọc tới ACK.
                     */
                    while (commandChannel
                               .Commands
                               .TryDequeue(
                                   out WorkstationCommandType
                                       commandType))
                    {
                        string commandMessage =
                            ProtocolJson.Serialize(
                                MessageTypes
                                    .WorkstationCommand,
                                new WorkstationCommandPayload(
                                    commandType,
                                    DateTime.UtcNow));

                        await writer
                            .WriteLineAsync(
                                commandMessage);
                    }

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
        }
        catch (OperationCanceledException)
            when (cancellationToken
                .IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        catch (SocketException)
        {
        }
        finally
        {
            client.Dispose();

            if (connectionInfo is not null
                && commandChannel is not null)
            {
                lock (_commandChannelsLock)
                {
                    if (_commandChannels
                            .TryGetValue(
                                connectionInfo.MachineId,
                                out ClientCommandChannel?
                                    current)
                        && ReferenceEquals(
                            current,
                            commandChannel))
                    {
                        _commandChannels.Remove(
                            connectionInfo.MachineId);
                    }
                }
            }

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

        lock (_commandChannelsLock)
        {
            _commandChannels.Clear();
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

    private sealed class ClientCommandChannel
    {
        public ConcurrentQueue<
            WorkstationCommandType>
            Commands
        {
            get;
        } =
            new();
    }
}