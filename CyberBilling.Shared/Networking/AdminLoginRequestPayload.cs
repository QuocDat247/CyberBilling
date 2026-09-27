namespace CyberBilling.Shared.Networking;

public sealed record AdminLoginRequestPayload(
    string Username,
    string Password);