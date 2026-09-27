namespace CyberBilling.Client.Networking;

public sealed class
    ClientConnectionStateChangedEventArgs :
        EventArgs
{
    public ClientConnectionStateChangedEventArgs(
        ClientConnectionState state,
        string message)
    {
        State =
            state;

        Message =
            message;
    }

    public ClientConnectionState State
    {
        get;
    }

    public string Message
    {
        get;
    }
}