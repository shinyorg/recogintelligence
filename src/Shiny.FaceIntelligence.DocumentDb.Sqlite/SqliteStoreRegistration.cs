using Shiny.DocumentDb.Sqlite;

namespace Shiny.FaceIntelligence.DocumentDb.Sqlite;

public static class SqliteStoreRegistration
{
    /// <summary>
    /// Use a SQLite + sqlite-vec face store. Convenience over <c>UseDocumentDbStore</c> that wires a
    /// vector-enabled <see cref="SqliteDatabaseProvider"/>. Bundle the matching <c>vec0</c> native binary.
    /// </summary>
    public static FaceIntelligenceRegistrationBuilder UseSqliteStore(
        this FaceIntelligenceRegistrationBuilder builder,
        Action<SqliteFaceStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SqliteFaceStoreOptions();
        configure(options);

        return builder.UseDocumentDbStore(_ => new SqliteDatabaseProvider(options.ConnectionString)
        {
            EnableVectorExtension = true,
            VectorExtensionPath = options.VectorExtensionPath
        });
    }
}
