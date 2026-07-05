using Shiny.DocumentDb.Sqlite;
using Shiny.DocumentDb.Sqlite.VectorSupport;

namespace Shiny.VoiceIntelligence.DocumentDb.Sqlite;

public static class SqliteStoreRegistration
{
    /// <summary>
    /// Use a SQLite + sqlite-vec voice store. Convenience over <c>UseDocumentDbStore</c> that wires a
    /// vector-enabled <see cref="SqliteDatabaseProvider"/>. Bundle the matching <c>vec0</c> native binary.
    /// </summary>
    public static VoiceIntelligenceRegistrationBuilder UseSqliteStore(
        this VoiceIntelligenceRegistrationBuilder builder,
        Action<SqliteVoiceStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SqliteVoiceStoreOptions();
        configure(options);

        // SqliteVec.CreateProvider calls SqliteVec.RegisterAutoExtension() (which wires
        // sqlite3_auto_extension(sqlite3_vec_init) so the vec0 virtual table is available on every
        // connection) and returns a provider with VectorExtensionPreloaded set. This is the only
        // approach that works on iOS, where the extension is statically linked and dlopen of a loose
        // .dylib is forbidden — merely setting VectorExtensionPreloaded skips LoadExtension but never
        // registers vec0, surfacing as "no such module: vec0" on first enroll.
        return builder.UseDocumentDbStore(_ => SqliteVec.CreateProvider(options.ConnectionString));
    }
}
