namespace CyberBilling.Shared.Networking;

public static class MessageTypes
{
    public const string ClientHello =
        "client.hello";

    public const string Heartbeat =
        "client.heartbeat";

    public const string HeartbeatAck =
        "server.heartbeat-ack";

    public const string ClientShutdown =
        "client.shutdown";
}