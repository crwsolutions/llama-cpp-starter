using CommunityToolkit.Maui.Storage;
using LlamaCppStarterApp.Services;
using Microsoft.Data.Sqlite;

namespace LlamaCppStarterApp.Repositories;

internal static class Database
{
    internal static string DbPath { get; private set; } = default!;

    internal const int CurrentUserVersion = 2;

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

        // 0 → 2: new tables for models/profiles/runtimes/settings.
        // CreateCoreTables/SeedSettings create the full v2 schema directly
        // (incl. ModelId/MetadataJson/CapabilitiesJson + GlobalLaunchDefaults) → one step.
        if (userVersion < 1)
        {
            CreateCoreTables(connection);
            SeedSettings(connection);
            SetUserVersion(connection, CurrentUserVersion);
            return;
        }

        // 1 → 2: model metadata + capabilities (ModelId/MetadataJson/CapabilitiesJson)
        //         + app-global launch defaults as an AppSettings row
        if (userVersion < 2)
        {
            MigrateToVersion2(connection);
            SetUserVersion(connection, 2);
        }
    }

    private static void CreateDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        CreateCoreTables(connection);
        SeedSettings(connection);
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
