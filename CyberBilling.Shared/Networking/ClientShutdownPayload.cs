namespace CyberBilling.Shared.Networking;

public sealed record ClientShutdownPayload(
    string MachineId,
    DateTime SentAtUtc);