using Shiny.DocumentDb;

namespace Shiny.VoiceIntelligence.DocumentDb;

/// <summary>
/// <see cref="IVoiceStore"/> backed by a Shiny.DocumentDb <see cref="IDocumentStore"/> with
/// <see cref="Speaker.Embedding"/> mapped for vector (ANN) search. The embedding lives in the provider's
/// vector sidecar; recognition reads it back via <c>NearestVectors</c>. Mirrors <c>DocumentDbFaceStore</c>.
/// </summary>
/// <remarks>
/// The <see cref="IDocumentStore"/> is passed in fully constructed — cheap and safe to build eagerly,
/// because <c>new DocumentStore(...)</c> only creates the connection object and resolves mapping metadata.
/// Opening the connection and loading the native vector extension (e.g. sqlite-vec's <c>vec0</c>) is
/// deferred by the store <b>itself</b> to the first operation — i.e. inside an enroll/recognize call where
/// the pages catch any failure — so this adapter needs no laziness of its own.
/// </remarks>
public class DocumentDbVoiceStore(IDocumentStore store) : IVoiceStore
{
    public Task Add(Speaker speaker, CancellationToken ct = default)
        => store.Insert(speaker, cancellationToken: ct);

    public async Task<IReadOnlyList<VoiceMatch>> FindNearest(ReadOnlyMemory<float> embedding, int count, CancellationToken ct = default)
    {
        var hits = await store.NearestVectors<Speaker>(embedding, count, cancellationToken: ct);
        var matches = new List<VoiceMatch>(hits.Count);
        foreach (var hit in hits)
            matches.Add(new VoiceMatch(hit.Document, hit.Score));
        return matches;
    }

    public Task<IReadOnlyList<Speaker>> GetAll(CancellationToken ct = default)
        => store.Query<Speaker>().OrderByDescending(p => p.EnrolledAt).ToList(ct);

    public Task<int> RemoveByPersonIdentifier(string personIdentifier, CancellationToken ct = default)
        => store.Query<Speaker>().Where(s => s.PersonIdentifier == personIdentifier).ExecuteDelete(ct);
}
