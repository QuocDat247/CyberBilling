namespace CyberBilling.Shared.Networking;

public sealed record ClientHelloPayload(
    string MachineId,
    string MachineName);