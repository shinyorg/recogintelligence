using Microsoft.Extensions.DependencyInjection;
using Shiny.DocumentDb;

namespace Shiny.FaceIntelligence.DocumentDb;

public static class DocumentDbStoreRegistration
{
    /// <summary>
    /// Use a Shiny.DocumentDb store for faces. Supply the database provider (e.g. Sqlite, Postgres)
    /// via <paramref name="databaseProviderFactory"/>. The vector dimension is read from the registered
    /// <see cref="IFaceEmbedder"/>, so it always matches the model.
    /// </summary>
    public static FaceIntelligenceRegistrationBuilder UseDocumentDbStore(
        this FaceIntelligenceRegistrationBuilder builder,
        Func<IServiceProvider, IDatabaseProvider> databaseProviderFactory)
    {
        ArgumentNullException.ThrowIfNull(databaseProviderFactory);

        // The IDocumentStore is PRIVATE to this face store — it is NOT registered in the container.
        // Registering a shared IDocumentStore would collide with any other DocumentDb store in the same
        // app (e.g. the voice stack): GetRequiredService<IDocumentStore> returns the last registration,
        // so faces would end up writing to whichever store registered last. Each stack owns its own.
        return builder.UseStore(sp =>
        {
            var embedder = sp.GetRequiredService<IFaceEmbedder>();
            var options = new DocumentStoreOptions
            {
                DatabaseProvider = databaseProviderFactory(sp),
                JsonSerializerOptions = FacesJsonContext.Default.Options,
                UseReflectionFallback = false
            };
            // Vector dimension must match the model's output (read from the embedder).
            options.MapVectorProperty<Person>(p => p.Embedding, embedder.Dimensions, VectorDistance.Cosine);

            // Build eagerly — cheap. new DocumentStore only creates the connection object + mapping
            // metadata; the DB connection + vec0 load are deferred by DocumentStore to the first
            // operation (inside enroll/recognize), which is where the pages catch any failure.
            return new DocumentDbFaceStore(new DocumentStore(options));
        });
    }
}
