namespace Shiny.VoiceIntelligence.DocumentDb.Sqlite;

/// <summary>Configures the SQLite (sqlite-vec) voice store.</summary>
public class SqliteVoiceStoreOptions
{
    /// <summary>SQLite connection string for the voice database.</summary>
    public string ConnectionString { get; set; } = "Data Source=voices.db";
}
