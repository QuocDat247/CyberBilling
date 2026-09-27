namespace CyberBilling.Shared.Networking;

public sealed record HeartbeatAckPayload(
    DateTime ServerTimeUtc);