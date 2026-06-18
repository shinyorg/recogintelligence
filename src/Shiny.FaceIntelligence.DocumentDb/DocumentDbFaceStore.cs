using Shiny.DocumentDb;

namespace Shiny.FaceIntelligence.DocumentDb;

/// <summary>
/// <see cref="IFaceStore"/> backed by a Shiny.DocumentDb <see cref="IDocumentStore"/> with
/// <see cref="Person.Embedding"/> mapped for vector (ANN) search. The embedding lives in the
/// provider's vector sidecar; recognition reads it back via <c>NearestVectors</c>.
/// </summary>
/// <remarks>
/// The underlying <see cref="IDocumentStore"/> is resolved <b>lazily</b>: building it can open a database
/// connection and load a native vector extension (e.g. sqlite-vec's <c>vec0</c>), which would otherwise throw
/// while the DI container constructs this store — i.e. during ViewModel construction, a launch/navigation
/// crash. Deferring to the first store operation moves any such failure into an enroll/recognize call where
/// the pages catch it. Mirrors the lazy model load in <c>OnnxArcFaceEmbedder</c>.
/// </remarks>
public class DocumentDbFaceStore(Lazy<IDocumentStore> store) : IFaceStore
{
    /// <summary>Convenience for direct (non-lazy) construction, e.g. server-side where eager init is fine.</summary>
    public DocumentDbFaceStore(IDocumentStore store) : this(new Lazy<IDocumentStore>(() => store)) { }

    IDocumentStore Store => store.Value;

    public Task Add(Person person, CancellationToken ct = default)
        => this.Store.Insert(person, cancellationToken: ct);

    public async Task<IReadOnlyList<FaceMatch>> FindNearest(ReadOnlyMemory<float> embedding, int count, CancellationToken ct = default)
    {
        var hits = await this.Store.NearestVectors<Person>(embedding, count, cancellationToken: ct);
        var matches = new List<FaceMatch>(hits.Count);
        foreach (var hit in hits)
            matches.Add(new FaceMatch(hit.Document, hit.Score));
        return matches;
    }

    public Task<IReadOnlyList<Person>> GetAll(CancellationToken ct = default)
        => this.Store.Query<Person>().OrderByDescending(p => p.EnrolledAt).ToList(ct);

    public Task<int> RemoveByName(string name, CancellationToken ct = default)
        => this.Store.Query<Person>().Where(p => p.Name == name).ExecuteDelete(ct);
}
