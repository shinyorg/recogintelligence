namespace Shiny.FaceIntelligence.DocumentDb.Sqlite;

/// <summary>Configures the SQLite (sqlite-vec) face store.</summary>
public class SqliteFaceStoreOptions
{
    /// <summary>SQLite connection string for the face database.</summary>
    public string ConnectionString { get; set; } = "Data Source=faces.db";

    /// <summary>
    /// Path/filename of the sqlite-vec extension binary (<c>vec0</c>). The loader searches OS paths and
    /// the app directory; on mobile you typically bundle <c>vec0.dylib</c>/<c>vec0.so</c> and point here.
    /// </summary>
    public string VectorExtensionPath { get; set; } = "vec0";
}
