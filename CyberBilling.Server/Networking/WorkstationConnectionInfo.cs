namespace CyberBilling.Server.Networking;

public sealed record WorkstationConnectionInfo(
    string MachineId,
    string MachineName,
    string IpAddress,
    WorkstationConnectionState State,
    DateTime ConnectedAt,
    DateTime LastSeen);