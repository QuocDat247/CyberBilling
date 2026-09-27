namespace CyberBilling.Shared.Networking;

public sealed record HeartbeatPayload(
    string MachineId,
    DateTime SentAtUtc);