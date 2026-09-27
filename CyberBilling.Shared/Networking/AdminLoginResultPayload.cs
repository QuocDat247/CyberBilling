namespace CyberBilling.Shared.Networking;

public sealed record AdminLoginResultPayload(
    bool Success,
    string Message);