using Microsoft.Extensions.DependencyInjection;
using Shiny.DocumentDb;

namespace Shiny.VoiceIntelligence.DocumentDb;

public static class DocumentDbStoreRegistration
{
    /// <summary>
    /// Use a Shiny.DocumentDb store for voices. Supply the database provider (e.g. Sqlite, Postgres)
    /// via <paramref name="databaseProviderFactory"/>. The vector dimension is read from the registered
    /// <see cref="ISpeakerEmbedder"/>, so it always matches the model.
    /// </summary>
    public static VoiceIntelligenceRegistrationBuilder UseDocumentDbStore(
        this VoiceIntelligenceRegistrationBuilder builder,
        Func<IServiceProvider, IDatabaseProvider> databaseProviderFactory)
    {
        ArgumentNullException.ThrowIfNull(databaseProviderFactory);

        // The IDocumentStore is PRIVATE to this voice store — it is NOT registered in the container.
        // Registering a shared IDocumentStore would collide with any other DocumentDb store in the same
        // app (e.g. the face stack): GetRequiredService<IDocumentStore> returns the last registration,
        // so voices would end up writing to whichever store registered last. Each stack owns its own.
        return builder.UseStore(sp =>
        {
            var embedder = sp.GetRequiredService<ISpeakerEmbedder>();
            var options = new DocumentStoreOptions
            {
                DatabaseProvider = databaseProviderFactory(sp),
                JsonSerializerOptions = VoicesJsonContext.Default.Options,
                UseReflectionFallback = false
            };
            // Vector dimension must match the model's output (read from the embedder).
            options.MapVectorProperty<Speaker>(p => p.Embedding, embedder.Dimensions, VectorDistance.Cosine);

            // Build eagerly — cheap. new DocumentStore only creates the connection object + mapping
            // metadata; the DB connection + vec0 load are deferred by DocumentStore to the first
            // operation (inside enroll/recognize), which is where the pages catch any failure.
            return new DocumentDbVoiceStore(new DocumentStore(options));
        });
    }
}
