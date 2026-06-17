using Shiny.DocumentDb;

namespace Shiny.FaceIntelligence.DocumentDb;

/// <summary>
/// <see cref="IFaceStore"/> backed by a Shiny.DocumentDb <see cref="IDocumentStore"/> with
/// <see cref="Person.Embedding"/> mapped for vector (ANN) search. The embedding lives in the
/// provider's vector sidecar; recognition reads it back via <c>NearestVectors</c>.
/// </summary>
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
