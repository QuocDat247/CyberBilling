namespace CyberBilling.Shared.Networking;

public sealed record ProtocolMessage(
    string Type,
    string Payload);