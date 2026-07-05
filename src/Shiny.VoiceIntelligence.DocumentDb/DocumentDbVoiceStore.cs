using Shiny.DocumentDb;

namespace Shiny.VoiceIntelligence.DocumentDb;

/// <summary>
/// <see cref="IVoiceStore"/> backed by a Shiny.DocumentDb <see cref="IDocumentStore"/> with
/// <see cref="Speaker.Embedding"/> mapped for vector (ANN) search. The embedding lives in the provider's
/// vector sidecar; recognition reads it back via <c>NearestVectors</c>. Mirrors <c>DocumentDbFaceStore</c>.
/// </summary>
/// <remarks>
/// The underlying <see cref="IDocumentStore"/> is resolved <b>lazily</b>: building it can open a database
/// connection and load a native vector extension (e.g. sqlite-vec's <c>vec0</c>), which would otherwise throw
/// while the DI container constructs this store — i.e. during ViewModel construction, a launch/navigation
/// crash. Deferring to the first store operation moves any such failure into an enroll/recognize call where
/// the pages catch it. Mirrors the lazy model load in <c>OnnxEcapaEmbedder</c>.
/// </remarks>
public class DocumentDbVoiceStore(Lazy<IDocumentStore> store) : IVoiceStore
{
    /// <summary>Convenience for direct (non-lazy) construction, e.g. server-side where eager init is fine.</summary>
    public DocumentDbVoiceStore(IDocumentStore store) : this(new Lazy<IDocumentStore>(() => store)) { }

    IDocumentStore Store => store.Value;

    public Task Add(Speaker speaker, CancellationToken ct = default)
        => this.Store.Insert(speaker, cancellationToken: ct);

    public async Task<IReadOnlyList<VoiceMatch>> FindNearest(ReadOnlyMemory<float> embedding, int count, CancellationToken ct = default)
    {
        var hits = await this.Store.NearestVectors<Speaker>(embedding, count, cancellationToken: ct);
        var matches = new List<VoiceMatch>(hits.Count);
        foreach (var hit in hits)
            matches.Add(new VoiceMatch(hit.Document, hit.Score));
        return matches;
    }

    public Task<IReadOnlyList<Speaker>> GetAll(CancellationToken ct = default)
        => this.Store.Query<Speaker>().OrderByDescending(p => p.EnrolledAt).ToList(ct);

    public Task<int> RemoveByName(string name, CancellationToken ct = default)
        => this.Store.Query<Speaker>().Where(p => p.Name == name).ExecuteDelete(ct);
}
