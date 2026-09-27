using CyberBilling.Server.Models;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;

namespace CyberBilling.Server.Persistence;

public sealed class CyberBillingDatabase
{
    private readonly string _databasePath;

    private readonly string _connectionString;

    public CyberBillingDatabase()
    {
        string directory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder
                        .LocalApplicationData),
                "CyberBilling",
                "Server");

        Directory.CreateDirectory(
            directory);

        _databasePath =
            Path.Combine(
                directory,
                "cyberbilling.db");

        _connectionString =
            new SqliteConnectionStringBuilder
            {
                DataSource =
                    _databasePath,

                Mode =
                    SqliteOpenMode
                        .ReadWriteCreate,

                Cache =
                    SqliteCacheMode.Shared
            }
            .ToString();
    }

    public string DatabasePath =>
        _databasePath;

    public void Initialize()
    {
        using SqliteConnection connection =
            OpenConnection();

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS BillingSettings
            (
                Id INTEGER NOT NULL PRIMARY KEY,
                HourlyRate INTEGER NOT NULL,
                MinimumCharge INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Workstations
            (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                MachineId TEXT NOT NULL UNIQUE,
                WorkstationNumber INTEGER NOT NULL UNIQUE,
                MachineName TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Sessions
            (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                MachineId TEXT NOT NULL,
                Mode INTEGER NOT NULL,
                StartedAtUtc TEXT NOT NULL,
                HourlyRate INTEGER NOT NULL,
                PrepaidAmount INTEGER NOT NULL DEFAULT 0,
                ServiceAmount INTEGER NOT NULL DEFAULT 0,
                Status TEXT NOT NULL,
                EndedAtUtc TEXT NULL,
                PaidAtUtc TEXT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS
                UX_Sessions_ActiveMachine
            ON Sessions(MachineId)
            WHERE Status = 'Active';

            INSERT OR IGNORE INTO BillingSettings
            (
                Id,
                HourlyRate,
                MinimumCharge
            )
            VALUES
            (
                1,
                6000,
                1000
            );
            """;

        command.ExecuteNonQuery();
    }

    public BillingSettingsSnapshot
        LoadSettings()
    {
        using SqliteConnection connection =
            OpenConnection();

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                HourlyRate,
                MinimumCharge
            FROM BillingSettings
            WHERE Id = 1;
            """;

        using SqliteDataReader reader =
            command.ExecuteReader();

        if (!reader.Read())
        {
            return new BillingSettingsSnapshot(
                6000m,
                1000m);
        }

        return new BillingSettingsSnapshot(
            reader.GetInt64(0),
            reader.GetInt64(1));
    }

    public void SavePricingAndApplyToActiveSessions(
        decimal hourlyRate,
        decimal minimumCharge)
    {
        using SqliteConnection connection =
            OpenConnection();

        using SqliteTransaction transaction =
            connection.BeginTransaction();

        using (SqliteCommand command =
               connection.CreateCommand())
        {
            command.Transaction =
                transaction;

            command.CommandText =
                """
                UPDATE BillingSettings
                SET
                    HourlyRate = $hourlyRate,
                    MinimumCharge = $minimumCharge
                WHERE Id = 1;
                """;

            command.Parameters.AddWithValue(
                "$hourlyRate",
                DecimalToInteger(
                    hourlyRate));

            command.Parameters.AddWithValue(
                "$minimumCharge",
                DecimalToInteger(
                    minimumCharge));

            command.ExecuteNonQuery();
        }

        /*
         * Yêu cầu nghiệp vụ:
         * đổi giá giờ thì toàn bộ phiên
         * đang chạy nhận giá mới ngay.
         */
        using (SqliteCommand command =
               connection.CreateCommand())
        {
            command.Transaction =
                transaction;

            command.CommandText =
                """
                UPDATE Sessions
                SET HourlyRate = $hourlyRate
                WHERE Status = 'Active';
                """;

            command.Parameters.AddWithValue(
                "$hourlyRate",
                DecimalToInteger(
                    hourlyRate));

            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<WorkstationSnapshot>
        LoadWorkstations()
    {
        List<WorkstationSnapshot> result =
            new();

        using SqliteConnection connection =
            OpenConnection();

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                WorkstationNumber,
                MachineId,
                MachineName
            FROM Workstations
            ORDER BY WorkstationNumber;
            """;

        using SqliteDataReader reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            result.Add(
                new WorkstationSnapshot(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2)));
        }

        return result;
    }

    public void UpsertWorkstation(
        int workstationNumber,
        string machineId,
        string machineName)
    {
        using SqliteConnection connection =
            OpenConnection();

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO Workstations
            (
                MachineId,
                WorkstationNumber,
                MachineName
            )
            VALUES
            (
                $machineId,
                $workstationNumber,
                $machineName
            )
            ON CONFLICT(MachineId)
            DO UPDATE SET
                MachineName =
                    excluded.MachineName;
            """;

        command.Parameters.AddWithValue(
            "$machineId",
            machineId);

        command.Parameters.AddWithValue(
            "$workstationNumber",
            workstationNumber);

        command.Parameters.AddWithValue(
            "$machineName",
            machineName);

        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ActiveSessionSnapshot>
        LoadActiveSessions()
    {
        List<ActiveSessionSnapshot> result =
            new();

        using SqliteConnection connection =
            OpenConnection();

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                Id,
                MachineId,
                Mode,
                StartedAtUtc,
                HourlyRate,
                PrepaidAmount,
                ServiceAmount
            FROM Sessions
            WHERE Status = 'Active'
            ORDER BY Id;
            """;

        using SqliteDataReader reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            result.Add(
                new ActiveSessionSnapshot(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    (SessionBillingMode)
                        reader.GetInt32(2),
                    FromDatabaseDateTime(
                        reader.GetString(3)),
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.GetInt64(6)));
        }

        return result;
    }

    public long CreateActiveSession(
        string machineId,
        SessionBillingMode mode,
        DateTime startedAt,
        decimal hourlyRate,
        decimal prepaidAmount,
        decimal serviceAmount)
    {
        using SqliteConnection connection =
            OpenConnection();

        using (SqliteCommand command =
               connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO Sessions
                (
                    MachineId,
                    Mode,
                    StartedAtUtc,
                    HourlyRate,
                    PrepaidAmount,
                    ServiceAmount,
                    Status
                )
                VALUES
                (
                    $machineId,
                    $mode,
                    $startedAtUtc,
                    $hourlyRate,
                    $prepaidAmount,
                    $serviceAmount,
                    'Active'
                );
                """;

            command.Parameters.AddWithValue(
                "$machineId",
                machineId);

            command.Parameters.AddWithValue(
                "$mode",
                (int)mode);

            command.Parameters.AddWithValue(
                "$startedAtUtc",
                ToDatabaseDateTime(
                    startedAt));

            command.Parameters.AddWithValue(
                "$hourlyRate",
                DecimalToInteger(
                    hourlyRate));

            command.Parameters.AddWithValue(
                "$prepaidAmount",
                DecimalToInteger(
                    prepaidAmount));

            command.Parameters.AddWithValue(
                "$serviceAmount",
                DecimalToInteger(
                    serviceAmount));

            command.ExecuteNonQuery();
        }

        using SqliteCommand idCommand =
            connection.CreateCommand();

        idCommand.CommandText =
            "SELECT last_insert_rowid();";

        object? result =
            idCommand.ExecuteScalar();

        return Convert.ToInt64(
            result,
            CultureInfo.InvariantCulture);
    }

    public void MarkSessionsPaid(
        IEnumerable<long> sessionIds)
    {
        long[] ids =
            sessionIds
                .Distinct()
                .ToArray();

        if (ids.Length == 0)
        {
            return;
        }

        using SqliteConnection connection =
            OpenConnection();

        using SqliteTransaction transaction =
            connection.BeginTransaction();

        foreach (long sessionId in ids)
        {
            using SqliteCommand command =
                connection.CreateCommand();

            command.Transaction =
                transaction;

            command.CommandText =
                """
                UPDATE Sessions
                SET
                    Status = 'Paid',
                    EndedAtUtc = $endedAtUtc,
                    PaidAtUtc = $paidAtUtc
                WHERE
                    Id = $id
                    AND Status = 'Active';
                """;

            string now =
                ToDatabaseDateTime(
                    DateTime.Now);

            command.Parameters.AddWithValue(
                "$endedAtUtc",
                now);

            command.Parameters.AddWithValue(
                "$paidAtUtc",
                now);

            command.Parameters.AddWithValue(
                "$id",
                sessionId);

            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private SqliteConnection OpenConnection()
    {
        SqliteConnection connection =
            new(
                _connectionString);

        connection.Open();

        return connection;
    }

    private static long DecimalToInteger(
        decimal value)
    {
        return decimal.ToInt64(
            decimal.Round(
                value,
                0,
                MidpointRounding
                    .AwayFromZero));
    }

    private static string ToDatabaseDateTime(
        DateTime value)
    {
        return value
            .ToUniversalTime()
            .ToString(
                "O",
                CultureInfo.InvariantCulture);
    }

    private static DateTime FromDatabaseDateTime(
        string value)
    {
        DateTime utc =
            DateTime.Parse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);

        return utc.ToLocalTime();
    }
}

public sealed record BillingSettingsSnapshot(
    decimal HourlyRate,
    decimal MinimumCharge);

public sealed record WorkstationSnapshot(
    int WorkstationNumber,
    string MachineId,
    string MachineName);

public sealed record ActiveSessionSnapshot(
    long Id,
    string MachineId,
    SessionBillingMode Mode,
    DateTime StartedAt,
    decimal HourlyRate,
    decimal PrepaidAmount,
    decimal ServiceAmount);