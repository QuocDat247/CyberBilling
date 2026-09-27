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

    public const string WorkstationCommand =
        "server.workstation-command";

    public const string AdminLoginRequest =
        "client.admin-login";

    public const string AdminLoginResult =
        "server.admin-login-result";
}