using System.IO;
using System.Text.Json;

namespace CyberBilling.Client.Configuration;

public sealed class ClientSettingsStore
{
    private static readonly JsonSerializerOptions
        JsonOptions =
            new()
            {
                WriteIndented =
                    true
            };

    private readonly string
        _settingsPath;

    public ClientSettingsStore()
    {
        string directory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder
                        .LocalApplicationData),
                "CyberBilling",
                "Client");

        Directory.CreateDirectory(
            directory);

        _settingsPath =
            Path.Combine(
                directory,
                "client-settings.json");
    }

    public string SettingsPath =>
        _settingsPath;

    public ClientSettings? Load()
    {
        try
        {
            if (!File.Exists(
                    _settingsPath))
            {
                return null;
            }

            string json =
                File.ReadAllText(
                    _settingsPath);

            ClientSettings? settings =
                JsonSerializer
                    .Deserialize<
                        ClientSettings>(
                            json,
                            JsonOptions);

            if (settings is null
                || string.IsNullOrWhiteSpace(
                    settings.ServerAddress)
                || string.IsNullOrWhiteSpace(
                    settings.MachineName))
            {
                return null;
            }

            return settings;
        }
        catch
        {
            return null;
        }
    }

    public void Save(
        ClientSettings settings)
    {
        string json =
            JsonSerializer.Serialize(
                settings,
                JsonOptions);

        File.WriteAllText(
            _settingsPath,
            json);
    }
}