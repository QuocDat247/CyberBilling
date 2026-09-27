namespace CyberBilling.Server.Networking;

public enum WorkstationConnectionState
{
    Online = 0,

    GracefulShutdown = 1,

    ConnectionLost = 2
}