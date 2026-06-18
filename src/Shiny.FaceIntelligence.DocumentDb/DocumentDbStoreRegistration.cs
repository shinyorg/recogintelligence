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

        builder.Services.AddSingleton<IDocumentStore>(sp =>
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
            return new DocumentStore(options);
        });

        // Resolve IDocumentStore lazily so building it (which may open the DB and load the native vector
        // extension, e.g. vec0) happens on the first enroll/recognize, not while DI constructs the store.
        return builder.UseStore(sp =>
            new DocumentDbFaceStore(new Lazy<IDocumentStore>(sp.GetRequiredService<IDocumentStore>)));
    }
}
