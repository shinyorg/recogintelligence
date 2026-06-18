namespace Shiny.FaceIntelligence.DocumentDb.Sqlite;

/// <summary>Configures the SQLite (sqlite-vec) face store.</summary>
public class SqliteFaceStoreOptions
{
    /// <summary>SQLite connection string for the face database.</summary>
    public string ConnectionString { get; set; } = "Data Source=faces.db";
}
