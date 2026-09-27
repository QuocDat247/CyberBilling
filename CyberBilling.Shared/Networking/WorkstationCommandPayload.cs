namespace CyberBilling.Shared.Networking;

public sealed record WorkstationCommandPayload(
    WorkstationCommandType Command,
    DateTime RequestedAtUtc);