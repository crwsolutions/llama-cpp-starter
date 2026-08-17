using CommunityToolkit.Maui.Storage;
using Microsoft.Data.Sqlite;

namespace LlamaCppStarterApp.Repositories;

internal static class Database
{
    internal static string DbPath { get; private set; } = default!;

    internal const int CurrentUserVersion = 1;

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

        // 0 → 1: nieuwe tabellen voor modellen/profielen/runtimes/settings
        if (userVersion < 1)
        {
            CreateCoreTables(connection);
            SeedSettings(connection);
            SetUserVersion(connection, 1);
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
            ScannedAt INTEGER
            );

            CREATE TABLE IF NOT EXISTS Profiles (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            ModelId INTEGER NOT NULL REFERENCES Models(Id) ON DELETE CASCADE,
            IsDefault INTEGER NOT NULL DEFAULT 0,
            Port INTEGER NOT NULL DEFAULT 8080,
            Params TEXT NOT NULL
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
        using var command = new SqliteCommand(
            """
            INSERT OR IGNORE INTO AppSettings (Key, Value) VALUES ('ModelsDirectory', 'E:\\llama.cpp\\models');
            INSERT OR IGNORE INTO AppSettings (Key, Value) VALUES ('RuntimeDirectory', 'E:\\llama.cpp\\llama-local-build');
            """, connection);
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
