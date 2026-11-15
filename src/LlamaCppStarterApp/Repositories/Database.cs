using System.Text.Json;
using CommunityToolkit.Maui.Storage;
using LlamaCppStarterApp.Services;
using Microsoft.Data.Sqlite;

namespace LlamaCppStarterApp.Repositories;

internal static class Database
{
    internal static string DbPath { get; private set; } = default!;

    internal const int CurrentUserVersion = 4;

    internal static void Initialize()
    {
        DbPath = Path.Combine(FileSystem.AppDataDirectory, "llamacppstarter_data.db");
        Debug.WriteLine("SQLite: " + DbPath);

        if (!File.Exists(DbPath))
        {
            CreateDatabase();
        }
        else
        {
            Migrate();
        }
    }

    private static void Migrate()
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        var userVersion = GetInt(connection, "PRAGMA user_version");

        // 0 → 2: create or upgrade the core schema, then continue with later migrations.
        if (userVersion < 1)
        {
            CreateCoreTables(connection);
            SeedSettings(connection);
            SetUserVersion(connection, 2);
            userVersion = 2;
        }

        // 1 → 2: model metadata + capabilities (ModelId/MetadataJson/CapabilitiesJson)
        //         + app-global launch defaults as an AppSettings row
        if (userVersion < 2)
        {
            MigrateToVersion2(connection);
            SetUserVersion(connection, 2);
            userVersion = 2;
        }

        // 2 → 3: benchmark configurations, run snapshots and raw measurements.
        if (userVersion < 3)
        {
            CreateBenchmarkTables(connection);
            SetUserVersion(connection, 3);
            userVersion = 3;
        }

        // 3 → 4: store benchmark repetition counts, comments, and runtime version per run.
        if (userVersion < 4)
        {
            MigrateToVersion4(connection);
            SetUserVersion(connection, 4);
        }
    }

    private static void CreateDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        CreateCoreTables(connection);
        SeedSettings(connection);
        CreateBenchmarkTables(connection);
        MigrateToVersion4(connection);
        SetUserVersion(connection, CurrentUserVersion);
    }

    /// <summary>
    /// Migration 1 → 2: three new columns on Models + ModelId backfill (deterministic
    /// from Path) + unique index; seeds the AppSettings row GlobalLaunchDefaults (INSERT OR IGNORE).
    /// Non-destructive and idempotent; existing data is left untouched.
    /// </summary>
    private static void MigrateToVersion2(SqliteConnection connection)
    {
        using (var command = new SqliteCommand(
            """
            ALTER TABLE Models ADD COLUMN ModelId TEXT NOT NULL DEFAULT '';
            ALTER TABLE Models ADD COLUMN MetadataJson TEXT;
            ALTER TABLE Models ADD COLUMN CapabilitiesJson TEXT;
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Models_ModelId ON Models(ModelId);
            """, connection))
        {
            command.ExecuteNonQuery();
        }

        // Backfill: deterministic ModelId per row (same formula as the scanner),
        // using the configured ModelsDirectory as scope root.
        var modelsRoot = GetSettingValue(connection, "ModelsDirectory");
        using (var select = new SqliteCommand("SELECT Id, Path FROM Models WHERE ModelId = ''", connection))
        using (var update = new SqliteCommand("UPDATE Models SET ModelId = @ModelId WHERE Id = @Id"))
        using (var exists = new SqliteCommand("SELECT COUNT(1) FROM Models WHERE ModelId = @ModelId AND Id <> @Id"))
        {
            var rows = new List<(int Id, string ModelId)>();
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                var path = reader.GetString(1);
                var scopeRoot = string.IsNullOrEmpty(modelsRoot)
                    ? Path.GetDirectoryName(path) ?? path
                    : modelsRoot;
                rows.Add((reader.GetInt32(0), ModelCompanionService.ModelIdForPath(scopeRoot, path)));
            }

            foreach (var (id, modelId) in rows)
            {
                // Unique index: never write a duplicate (cannot pre-exist, but skip if so).
                exists.Parameters.AddWithValue("@ModelId", modelId);
                exists.Parameters.AddWithValue("@Id", id);
                if ((int)exists.ExecuteScalar()! > 0) continue;

                update.Parameters.AddWithValue("@ModelId", modelId);
                update.Parameters.AddWithValue("@Id", id);
                update.ExecuteNonQuery();
            }
        }

        SeedSettings(connection);
    }

    private static string? GetSettingValue(SqliteConnection connection, string key)
    {
        using var command = new SqliteCommand("SELECT Value FROM AppSettings WHERE Key = @Key", connection);
        command.Parameters.AddWithValue("@Key", key);
        return command.ExecuteScalar() as string;
    }

    private static void CreateCoreTables(SqliteConnection connection)
    {
        using var command = new SqliteCommand(
            """
            CREATE TABLE IF NOT EXISTS Models (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Path TEXT UNIQUE NOT NULL,
            Name TEXT,
            Quant TEXT,
            SizeBytes INTEGER,
            MmprojPath TEXT NULL,
            ScannedAt INTEGER,
            ModelId TEXT NOT NULL DEFAULT '',
            MetadataJson TEXT,
            CapabilitiesJson TEXT
            );

            CREATE UNIQUE INDEX IF NOT EXISTS IX_Models_ModelId ON Models(ModelId);

            CREATE TABLE IF NOT EXISTS Profiles (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            ModelId INTEGER NOT NULL REFERENCES Models(Id) ON DELETE CASCADE,
            IsDefault INTEGER NOT NULL DEFAULT 0,
            Port INTEGER NOT NULL DEFAULT 8080,
            ParamsJson TEXT NOT NULL
            );

            CREATE UNIQUE INDEX IF NOT EXISTS IX_Profiles_Name_ModelId ON Profiles (Name, ModelId);

            CREATE TABLE IF NOT EXISTS Runtimes (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            ExecutablePath TEXT NOT NULL,
            Backend TEXT,
            Status TEXT,
            Location TEXT,
            CreatedAt INTEGER
            );

            CREATE UNIQUE INDEX IF NOT EXISTS IX_Runtimes_ExecutablePath ON Runtimes (ExecutablePath);

            CREATE TABLE IF NOT EXISTS AppSettings (
            Key TEXT PRIMARY KEY,
            Value TEXT
            );
            """, connection);
        command.ExecuteNonQuery();
    }

    private static void MigrateToVersion4(SqliteConnection connection)
    {
        using var command = new SqliteCommand(
            """
            ALTER TABLE BenchmarkRuns ADD COLUMN LlamaCppVersion TEXT NOT NULL DEFAULT '';
            ALTER TABLE BenchmarkRuns ADD COLUMN Repetitions INTEGER NOT NULL DEFAULT 3;
            ALTER TABLE BenchmarkRuns ADD COLUMN Warmups INTEGER NOT NULL DEFAULT 1;
            ALTER TABLE BenchmarkRuns ADD COLUMN Comment TEXT NOT NULL DEFAULT '';
            """, connection);
        command.ExecuteNonQuery();
        BackfillBenchmarkRunSettings(connection);
    }

    private static void BackfillBenchmarkRunSettings(SqliteConnection connection)
    {
        var snapshots = new List<(long Id, string Json)>();
        using (var select = new SqliteCommand("SELECT Id, ConfigurationSnapshotJson FROM BenchmarkRuns", connection))
        using (var reader = select.ExecuteReader())
        {
            while (reader.Read())
            {
                snapshots.Add((reader.GetInt64(0), reader.GetString(1)));
            }
        }

        using var update = new SqliteCommand(
            "UPDATE BenchmarkRuns SET Repetitions = @Repetitions, Warmups = @Warmups WHERE Id = @Id", connection);
        var repetitionsParameter = update.Parameters.Add("@Repetitions", Microsoft.Data.Sqlite.SqliteType.Integer);
        var warmupsParameter = update.Parameters.Add("@Warmups", Microsoft.Data.Sqlite.SqliteType.Integer);
        var idParameter = update.Parameters.Add("@Id", Microsoft.Data.Sqlite.SqliteType.Integer);
        foreach (var (id, json) in snapshots)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !TryReadSetting(document.RootElement, "Repetitions", out var repetitions)
                    || !TryReadSetting(document.RootElement, "Warmups", out var warmups))
                {
                    continue;
                }

                repetitionsParameter.Value = repetitions;
                warmupsParameter.Value = warmups;
                idParameter.Value = id;
                update.ExecuteNonQuery();
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"Could not migrate benchmark run {id} settings: {ex.Message}");
            }
        }
    }

    private static bool TryReadSetting(JsonElement element, string name, out int value)
    {
        value = 0;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.TryGetInt32(out value);
            }
        }
        return false;
    }

    private static void CreateBenchmarkTables(SqliteConnection connection)
    {
        using var command = new SqliteCommand(
            """
            CREATE TABLE IF NOT EXISTS BenchmarkConfigurations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                SettingsJson TEXT NOT NULL,
                CreatedAtUtc INTEGER NOT NULL,
                UpdatedAtUtc INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS BenchmarkRuns (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ConfigurationId INTEGER NULL,
                ConfigurationName TEXT NOT NULL,
                ConfigurationSnapshotJson TEXT NOT NULL,
                StartedAtUtc INTEGER NOT NULL,
                EndedAtUtc INTEGER NULL,
                Status TEXT NOT NULL,
                ModelName TEXT NOT NULL,
                ModelPath TEXT NOT NULL,
                ModelQuant TEXT NOT NULL,
                ProfileName TEXT NOT NULL,
                ProfileId INTEGER NOT NULL,
                RuntimeName TEXT NOT NULL,
                RuntimePath TEXT NOT NULL,
                BuildIdentity TEXT NOT NULL,
                ServerPid INTEGER NOT NULL,
                LaunchParametersJson TEXT NOT NULL,
                DatasetVersion TEXT NOT NULL,
                ErrorMessage TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_BenchmarkRuns_StartedAtUtc ON BenchmarkRuns(StartedAtUtc DESC);

            CREATE TABLE IF NOT EXISTS BenchmarkMeasurements (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RunId INTEGER NOT NULL REFERENCES BenchmarkRuns(Id) ON DELETE CASCADE,
                IsWarmup INTEGER NOT NULL,
                ScenarioId TEXT NOT NULL,
                ScenarioVersion TEXT NOT NULL,
                Category TEXT NOT NULL,
                Repetition INTEGER NOT NULL,
                PromptTokens INTEGER NULL,
                GeneratedTokens INTEGER NULL,
                CachedPromptTokens INTEGER NULL,
                CacheState TEXT NOT NULL,
                PromptMilliseconds REAL NULL,
                GenerationMilliseconds REAL NULL,
                TotalMilliseconds REAL NULL,
                ClientWallMilliseconds REAL NULL,
                PromptTokensPerSecond REAL NULL,
                GenerationTokensPerSecond REAL NULL,
                ToolCalls INTEGER NOT NULL,
                FailedToolCalls INTEGER NOT NULL,
                ErrorMessage TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_BenchmarkMeasurements_RunId ON BenchmarkMeasurements(RunId, Id);
            """, connection);
        command.ExecuteNonQuery();
    }

    private static void SeedSettings(SqliteConnection connection)
    {
        // GlobalLaunchDefaults = app-global defaults (exact reference command) as a JSON blob;
        // INSERT OR IGNORE → an existing value is left untouched (non-destructive).
        using var command = new SqliteCommand(
            $"""
            INSERT OR IGNORE INTO AppSettings (Key, Value) VALUES ('ModelsDirectory', 'E:\\llama.cpp\\models');
            INSERT OR IGNORE INTO AppSettings (Key, Value) VALUES ('RuntimeDirectory', 'E:\\llama.cpp\\llama-local-build');
            INSERT OR IGNORE INTO AppSettings (Key, Value) VALUES ('GlobalLaunchDefaults', @GlobalLaunchDefaults);
            """, connection);
        command.Parameters.AddWithValue("@GlobalLaunchDefaults", Models.ProfileParameters.GlobalLaunchDefaultsJson());
        command.ExecuteNonQuery();
    }

    private static int GetInt(SqliteConnection connection, string sql)
    {
        using var command = new SqliteCommand(sql, connection);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void SetUserVersion(SqliteConnection connection, int version)
    {
        using var command = new SqliteCommand($"PRAGMA user_version = {version};", connection);
        command.ExecuteNonQuery();
    }
}
