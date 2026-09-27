namespace CyberBilling.Client.Networking;

public enum ClientConnectionState
{
    Connecting = 0,

    Connected = 1,

    Reconnecting = 2,

    Stopped = 3
}