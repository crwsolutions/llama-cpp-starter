using CommunityToolkit.Maui.Storage;
using Microsoft.Data.Sqlite;

namespace LlamaCppStarterApp.Repositories;

internal static class Database
{
    internal static string DbPath { get; private set; } = default!;

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
    }

    private static void CreateDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        // Create PromptEntries table with auto-incrementing Id
        using var command = new SqliteCommand(
            """
            CREATE TABLE PromptEntries (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Prompt TEXT NOT NULL
            );
            """, connection);
        command.ExecuteNonQuery();
    }
}
