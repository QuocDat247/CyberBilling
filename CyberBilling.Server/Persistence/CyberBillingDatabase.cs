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
                PaymentId INTEGER NULL,
                BillableSeconds INTEGER NOT NULL DEFAULT 0,
                UsageAmount INTEGER NOT NULL DEFAULT 0,
                PausedAtUtc TEXT NULL,
                AccumulatedPausedSeconds INTEGER NOT NULL DEFAULT 0,
                Status TEXT NOT NULL,
                EndedAtUtc TEXT NULL,
                PaidAtUtc TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS Payments
            (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                PaidAtUtc TEXT NOT NULL,
                CalculatedAmount INTEGER NOT NULL,
                PaidAmount INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Services
            (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Category TEXT NOT NULL,
                Unit TEXT NOT NULL,
                Price INTEGER NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 1,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS SessionServices
            (
                SessionId INTEGER NOT NULL,
                ServiceId INTEGER NOT NULL,
                Quantity INTEGER NOT NULL DEFAULT 1,

                PRIMARY KEY
                (
                    SessionId,
                    ServiceId
                )
            );

            CREATE INDEX IF NOT EXISTS
                IX_SessionServices_SessionId
            ON SessionServices(SessionId);

            CREATE INDEX IF NOT EXISTS
                IX_Services_Active_Category
            ON Services(IsActive, Category);

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
        EnsurePauseColumns(
            connection);
        EnsurePaymentColumns(
            connection);
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

    public IReadOnlyList<SessionServiceSnapshot>
    LoadSessionServices(
        long sessionId)
    {
        List<SessionServiceSnapshot> result =
            new();

        using SqliteConnection connection =
            OpenConnection();

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
        SELECT
            ss.ServiceId,
            s.Name,
            s.Category,
            s.Unit,
            s.Price,
            ss.Quantity
        FROM SessionServices ss

        INNER JOIN Services s
            ON s.Id = ss.ServiceId

        WHERE ss.SessionId = $sessionId

        ORDER BY
            s.Category,
            s.Name;
        """;

        command.Parameters.AddWithValue(
            "$sessionId",
            sessionId);

        using SqliteDataReader reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            result.Add(
                new SessionServiceSnapshot(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt64(4),
                    reader.GetInt32(5)));
        }

        return result;
    }

    public decimal SaveSessionServices(
        long sessionId,
        IEnumerable<SessionServiceUpdate>
            services)
    {
        SessionServiceUpdate[] items =
            services
                .Where(
                    item =>
                        item.Quantity > 0)
                .GroupBy(
                    item =>
                        item.ServiceId)
                .Select(
                    group =>
                        new SessionServiceUpdate(
                            group.Key,
                            group.Sum(
                                item =>
                                    item.Quantity)))
                .ToArray();

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
            DELETE FROM SessionServices
            WHERE SessionId = $sessionId;
            """;

            command.Parameters.AddWithValue(
                "$sessionId",
                sessionId);

            command.ExecuteNonQuery();
        }

        foreach (SessionServiceUpdate item
                 in items)
        {
            using SqliteCommand command =
                connection.CreateCommand();

            command.Transaction =
                transaction;

            command.CommandText =
                """
            INSERT INTO SessionServices
            (
                SessionId,
                ServiceId,
                Quantity
            )
            VALUES
            (
                $sessionId,
                $serviceId,
                $quantity
            );
            """;

            command.Parameters.AddWithValue(
                "$sessionId",
                sessionId);

            command.Parameters.AddWithValue(
                "$serviceId",
                item.ServiceId);

            command.Parameters.AddWithValue(
                "$quantity",
                item.Quantity);

            command.ExecuteNonQuery();
        }

        decimal serviceAmount;

        using (SqliteCommand command =
               connection.CreateCommand())
        {
            command.Transaction =
                transaction;

            command.CommandText =
                """
            SELECT
                COALESCE(
                    SUM(
                        s.Price
                        * ss.Quantity),
                    0)
            FROM SessionServices ss

            INNER JOIN Services s
                ON s.Id = ss.ServiceId

            WHERE ss.SessionId = $sessionId;
            """;

            command.Parameters.AddWithValue(
                "$sessionId",
                sessionId);

            object? value =
                command.ExecuteScalar();

            serviceAmount =
                Convert.ToDecimal(
                    value,
                    CultureInfo.InvariantCulture);
        }

        using (SqliteCommand command =
               connection.CreateCommand())
        {
            command.Transaction =
                transaction;

            command.CommandText =
                """
            UPDATE Sessions
            SET ServiceAmount = $serviceAmount
            WHERE
                Id = $sessionId
                AND Status = 'Active';
            """;

            command.Parameters.AddWithValue(
                "$serviceAmount",
                DecimalToInteger(
                    serviceAmount));

            command.Parameters.AddWithValue(
                "$sessionId",
                sessionId);

            command.ExecuteNonQuery();
        }

        transaction.Commit();

        return serviceAmount;
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
                ServiceAmount,
                PausedAtUtc,
                AccumulatedPausedSeconds
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
                    reader.GetInt64(6),
                    reader.IsDBNull(7)
                        ? null
                        : FromDatabaseDateTime(
                            reader.GetString(7)),
                    reader.GetInt64(8)));
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

    public long CompletePayment(
    IEnumerable<SessionSettlement>
        settlements,
    decimal calculatedAmount,
    decimal paidAmount,
    DateTime paidAt)
    {
        SessionSettlement[] items =
            settlements.ToArray();

        if (items.Length == 0)
        {
            throw new InvalidOperationException(
                "Không có phiên nào để thanh toán.");
        }

        using SqliteConnection connection =
            OpenConnection();

        using SqliteTransaction transaction =
            connection.BeginTransaction();

        long paymentId;

        using (SqliteCommand command =
               connection.CreateCommand())
        {
            command.Transaction =
                transaction;

            command.CommandText =
                """
            INSERT INTO Payments
            (
                PaidAtUtc,
                CalculatedAmount,
                PaidAmount
            )
            VALUES
            (
                $paidAtUtc,
                $calculatedAmount,
                $paidAmount
            );
            """;

            command.Parameters.AddWithValue(
                "$paidAtUtc",
                ToDatabaseDateTime(
                    paidAt));

            command.Parameters.AddWithValue(
                "$calculatedAmount",
                DecimalToInteger(
                    calculatedAmount));

            command.Parameters.AddWithValue(
                "$paidAmount",
                DecimalToInteger(
                    paidAmount));

            command.ExecuteNonQuery();
        }

        using (SqliteCommand command =
               connection.CreateCommand())
        {
            command.Transaction =
                transaction;

            command.CommandText =
                "SELECT last_insert_rowid();";

            paymentId =
                Convert.ToInt64(
                    command.ExecuteScalar(),
                    CultureInfo.InvariantCulture);
        }

        string paidAtText =
            ToDatabaseDateTime(
                paidAt);

        foreach (SessionSettlement item
                 in items)
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
                PaymentId = $paymentId,
                EndedAtUtc = $endedAtUtc,
                PaidAtUtc = $paidAtUtc,
                BillableSeconds = $billableSeconds,
                UsageAmount = $usageAmount,
                ServiceAmount = $serviceAmount
            WHERE
                Id = $sessionId
                AND Status = 'Active';
            """;

            command.Parameters.AddWithValue(
                "$paymentId",
                paymentId);

            command.Parameters.AddWithValue(
                "$endedAtUtc",
                paidAtText);

            command.Parameters.AddWithValue(
                "$paidAtUtc",
                paidAtText);

            command.Parameters.AddWithValue(
                "$billableSeconds",
                item.BillableSeconds);

            command.Parameters.AddWithValue(
                "$usageAmount",
                DecimalToInteger(
                    item.UsageAmount));

            command.Parameters.AddWithValue(
                "$serviceAmount",
                DecimalToInteger(
                    item.ServiceAmount));

            command.Parameters.AddWithValue(
                "$sessionId",
                item.SessionId);

            command.ExecuteNonQuery();
        }

        transaction.Commit();

        return paymentId;
    }

    public IReadOnlyList<PaymentHistorySnapshot>
    LoadPaymentHistory()
    {
        List<PaymentHistorySnapshot> result =
            new();

        using SqliteConnection connection =
            OpenConnection();

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
        SELECT
            p.Id,

            GROUP_CONCAT(
                CASE
                    WHEN w.WorkstationNumber IS NULL
                        THEN s.MachineId
                    ELSE printf(
                        '%02d - %s',
                        w.WorkstationNumber,
                        w.MachineName)
                END,
                ' + '
            ),

            GROUP_CONCAT(
                CASE s.Mode
                    WHEN 1 THEN 'Trả sau'
                    WHEN 2 THEN 'Trả trước'
                    ELSE 'Khác'
                END,
                ' + '
            ),

            MIN(s.StartedAtUtc),
            MAX(s.EndedAtUtc),

            SUM(s.BillableSeconds),
            SUM(s.UsageAmount),
            SUM(s.ServiceAmount),

            p.CalculatedAmount,
            p.PaidAmount,
            p.PaidAtUtc

        FROM Payments p

        INNER JOIN Sessions s
            ON s.PaymentId = p.Id

        LEFT JOIN Workstations w
            ON w.MachineId = s.MachineId

        GROUP BY
            p.Id,
            p.CalculatedAmount,
            p.PaidAmount,
            p.PaidAtUtc

        ORDER BY p.Id DESC

        LIMIT 500;
        """;

        using SqliteDataReader reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            result.Add(
                new PaymentHistorySnapshot(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    FromDatabaseDateTime(
                        reader.GetString(3)),
                    FromDatabaseDateTime(
                        reader.GetString(4)),
                    reader.GetInt64(5),
                    reader.GetInt64(6),
                    reader.GetInt64(7),
                    reader.GetInt64(8),
                    reader.GetInt64(9),
                    FromDatabaseDateTime(
                        reader.GetString(10))));
        }

        return result;
    }

    public IReadOnlyList<ServiceSnapshot>
    LoadServices()
    {
        List<ServiceSnapshot> result =
            new();

        using SqliteConnection connection =
            OpenConnection();

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
        SELECT
            Id,
            Name,
            Category,
            Unit,
            Price
        FROM Services
        WHERE IsActive = 1
        ORDER BY
            Category,
            Name;
        """;

        using SqliteDataReader reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            result.Add(
                new ServiceSnapshot(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt64(4)));
        }

        return result;
    }

    public long AddService(
        string name,
        string category,
        string unit,
        decimal price)
    {
        using SqliteConnection connection =
            OpenConnection();

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
        INSERT INTO Services
        (
            Name,
            Category,
            Unit,
            Price,
            IsActive,
            CreatedAtUtc,
            UpdatedAtUtc
        )
        VALUES
        (
            $name,
            $category,
            $unit,
            $price,
            1,
            $createdAtUtc,
            $updatedAtUtc
        );
        """;

        string now =
            ToDatabaseDateTime(
                DateTime.Now);

        command.Parameters.AddWithValue(
            "$name",
            name.Trim());

        command.Parameters.AddWithValue(
            "$category",
            category);

        command.Parameters.AddWithValue(
            "$unit",
            unit.Trim());

        command.Parameters.AddWithValue(
            "$price",
            DecimalToInteger(
                price));

        command.Parameters.AddWithValue(
            "$createdAtUtc",
            now);

        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            now);

        command.ExecuteNonQuery();

        using SqliteCommand idCommand =
            connection.CreateCommand();

        idCommand.CommandText =
            "SELECT last_insert_rowid();";

        return Convert.ToInt64(
            idCommand.ExecuteScalar(),
            CultureInfo.InvariantCulture);
    }

    public void UpdateService(
        long id,
        string name,
        string category,
        string unit,
        decimal price)
    {
        using SqliteConnection connection =
            OpenConnection();

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
        UPDATE Services
        SET
            Name = $name,
            Category = $category,
            Unit = $unit,
            Price = $price,
            UpdatedAtUtc = $updatedAtUtc
        WHERE
            Id = $id
            AND IsActive = 1;
        """;

        command.Parameters.AddWithValue(
            "$name",
            name.Trim());

        command.Parameters.AddWithValue(
            "$category",
            category);

        command.Parameters.AddWithValue(
            "$unit",
            unit.Trim());

        command.Parameters.AddWithValue(
            "$price",
            DecimalToInteger(
                price));

        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            ToDatabaseDateTime(
                DateTime.Now));

        command.Parameters.AddWithValue(
            "$id",
            id);

        command.ExecuteNonQuery();
    }

    public void DeleteService(
        long id)
    {
        using SqliteConnection connection =
            OpenConnection();

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
        UPDATE Services
        SET
            IsActive = 0,
            UpdatedAtUtc = $updatedAtUtc
        WHERE Id = $id;
        """;

        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            ToDatabaseDateTime(
                DateTime.Now));

        command.Parameters.AddWithValue(
            "$id",
            id);

        command.ExecuteNonQuery();
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

    private static void EnsurePauseColumns(
    SqliteConnection connection)
    {
        HashSet<string> columns =
            new(
                StringComparer
                    .OrdinalIgnoreCase);

        using (SqliteCommand command =
               connection.CreateCommand())
        {
            command.CommandText =
                "PRAGMA table_info(Sessions);";

            using SqliteDataReader reader =
                command.ExecuteReader();

            while (reader.Read())
            {
                columns.Add(
                    reader.GetString(1));
            }
        }

        if (!columns.Contains(
                "PausedAtUtc"))
        {
            using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText =
                """
            ALTER TABLE Sessions
            ADD COLUMN PausedAtUtc TEXT NULL;
            """;

            command.ExecuteNonQuery();
        }

        if (!columns.Contains(
                "AccumulatedPausedSeconds"))
        {
            using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText =
                """
            ALTER TABLE Sessions
            ADD COLUMN AccumulatedPausedSeconds
                INTEGER NOT NULL DEFAULT 0;
            """;

            command.ExecuteNonQuery();
        }
    }
    public void PauseActiveSession(
    long sessionId,
    DateTime pausedAt)
    {
        using SqliteConnection connection =
            OpenConnection();

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
        UPDATE Sessions
        SET PausedAtUtc = $pausedAtUtc
        WHERE
            Id = $id
            AND Status = 'Active'
            AND PausedAtUtc IS NULL;
        """;

        command.Parameters.AddWithValue(
            "$pausedAtUtc",
            ToDatabaseDateTime(
                pausedAt));

        command.Parameters.AddWithValue(
            "$id",
            sessionId);

        command.ExecuteNonQuery();
    }

    public void ResumeActiveSession(
        long sessionId,
        long addedPausedSeconds)
    {
        using SqliteConnection connection =
            OpenConnection();

        using SqliteCommand command =
            connection.CreateCommand();

        command.CommandText =
            """
        UPDATE Sessions
        SET
            AccumulatedPausedSeconds =
                AccumulatedPausedSeconds
                + $seconds,
            PausedAtUtc = NULL
        WHERE
            Id = $id
            AND Status = 'Active';
        """;

        command.Parameters.AddWithValue(
            "$seconds",
            addedPausedSeconds);

        command.Parameters.AddWithValue(
            "$id",
            sessionId);

        command.ExecuteNonQuery();
    }

    private static void EnsurePaymentColumns(
    SqliteConnection connection)
    {
        HashSet<string> columns =
            new(
                StringComparer
                    .OrdinalIgnoreCase);

        using (SqliteCommand command =
               connection.CreateCommand())
        {
            command.CommandText =
                "PRAGMA table_info(Sessions);";

            using SqliteDataReader reader =
                command.ExecuteReader();

            while (reader.Read())
            {
                columns.Add(
                    reader.GetString(1));
            }
        }

        if (!columns.Contains(
                "PaymentId"))
        {
            using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText =
                """
            ALTER TABLE Sessions
            ADD COLUMN PaymentId INTEGER NULL;
            """;

            command.ExecuteNonQuery();
        }

        if (!columns.Contains(
                "BillableSeconds"))
        {
            using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText =
                """
            ALTER TABLE Sessions
            ADD COLUMN BillableSeconds
                INTEGER NOT NULL DEFAULT 0;
            """;

            command.ExecuteNonQuery();
        }

        if (!columns.Contains(
                "UsageAmount"))
        {
            using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText =
                """
            ALTER TABLE Sessions
            ADD COLUMN UsageAmount
                INTEGER NOT NULL DEFAULT 0;
            """;

            command.ExecuteNonQuery();
        }
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
    decimal ServiceAmount,
    DateTime? PausedAt,
    long AccumulatedPausedSeconds);

public sealed record SessionSettlement(
    long SessionId,
    long BillableSeconds,
    decimal UsageAmount,
    decimal ServiceAmount);

public sealed record PaymentHistorySnapshot(
    long PaymentId,
    string Machines,
    string Modes,
    DateTime StartedAt,
    DateTime EndedAt,
    long BillableSeconds,
    decimal UsageAmount,
    decimal ServiceAmount,
    decimal CalculatedAmount,
    decimal PaidAmount,
    DateTime PaidAt);

public sealed record ServiceSnapshot(
    long Id,
    string Name,
    string Category,
    string Unit,
    decimal Price);

public sealed record SessionServiceSnapshot(
    long ServiceId,
    string Name,
    string Category,
    string Unit,
    decimal Price,
    int Quantity);

public sealed record SessionServiceUpdate(
    long ServiceId,
    int Quantity);