using Shiny.DocumentDb;

namespace Shiny.FaceIntelligence.DocumentDb;

/// <summary>
/// <see cref="IFaceStore"/> backed by a Shiny.DocumentDb <see cref="IDocumentStore"/> with
/// <see cref="Person.Embedding"/> mapped for vector (ANN) search. The embedding lives in the
/// provider's vector sidecar; recognition reads it back via <c>NearestVectors</c>.
/// </summary>
/// <remarks>
/// The <see cref="IDocumentStore"/> is passed in fully constructed — cheap and safe to build eagerly,
/// because <c>new DocumentStore(...)</c> only creates the connection object and resolves mapping metadata.
/// Opening the connection and loading the native vector extension (e.g. sqlite-vec's <c>vec0</c>) is
/// deferred by the store <b>itself</b> to the first operation — i.e. inside an enroll/recognize call where
/// the pages catch any failure — so this adapter needs no laziness of its own.
/// </remarks>
public class DocumentDbFaceStore(IDocumentStore store) : IFaceStore
{
    public Task Add(Person person, CancellationToken ct = default)
        => store.Insert(person, cancellationToken: ct);

    public async Task<IReadOnlyList<FaceMatch>> FindNearest(ReadOnlyMemory<float> embedding, int count, CancellationToken ct = default)
    {
        var hits = await store.NearestVectors<Person>(embedding, count, cancellationToken: ct);
        var matches = new List<FaceMatch>(hits.Count);
        foreach (var hit in hits)
            matches.Add(new FaceMatch(hit.Document, hit.Score));
        return matches;
    }

    public Task<IReadOnlyList<Person>> GetAll(CancellationToken ct = default)
        => store.Query<Person>().OrderByDescending(p => p.EnrolledAt).ToList(ct);

    public Task<int> RemoveByName(string name, CancellationToken ct = default)
        => store.Query<Person>().Where(p => p.Name == name).ExecuteDelete(ct);
}
