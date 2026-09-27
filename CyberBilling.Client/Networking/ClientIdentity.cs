using System.IO;

namespace CyberBilling.Client.Networking;

public static class ClientIdentity
{
    private const string ApplicationFolder =
        "CyberBilling";

    private const string ClientFolder =
        "Client";

    private const string MachineIdFile =
        "machine-id.txt";

    public static string
        GetOrCreateMachineId()
    {
        string localApplicationData =
            Environment.GetFolderPath(
                Environment.SpecialFolder
                    .LocalApplicationData);

        string directory =
            Path.Combine(
                localApplicationData,
                ApplicationFolder,
                ClientFolder);

        Directory.CreateDirectory(
            directory);

        string path =
            Path.Combine(
                directory,
                MachineIdFile);

        if (File.Exists(path))
        {
            string existing =
                File.ReadAllText(path)
                    .Trim();

            if (Guid.TryParse(
                    existing,
                    out _))
            {
                return existing;
            }
        }

        string machineId =
            Guid.NewGuid()
                .ToString("D");

        File.WriteAllText(
            path,
            machineId);

        return machineId;
    }
}