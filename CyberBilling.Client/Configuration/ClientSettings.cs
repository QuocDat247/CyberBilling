namespace CyberBilling.Client.Configuration;

public sealed record ClientSettings(
    string ServerAddress,
    string MachineName);