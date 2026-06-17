namespace Shiny.FaceIntelligence;

/// <summary>
/// Default <see cref="IFaceIntelligence"/>: embeds faces with <see cref="IFaceEmbedder"/> and stores/matches
/// them via an <see cref="IFaceStore"/> (nearest-neighbor vector search), thresholded by
/// <see cref="FaceIntelligenceOptions.MaxDistance"/>.
/// </summary>
public class FaceIntelligenceManager(
    IFaceStore store,
    IFaceEmbedder embedder,
    FaceIntelligenceOptions options
) : IFaceIntelligence
{
    public async Task<Person> Enroll(string name, byte[] imageData, FaceBox face, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var embedding = embedder.Embed(imageData, face);
        var person = new Person
        {
            Id = Guid.NewGuid().ToString("n"), // string ids must be explicit
            Name = name.Trim(),
            Embedding = embedding,
            Thumbnail = FaceImaging.EncodeThumbnail(imageData, face),
            EnrolledAt = DateTimeOffset.UtcNow
        };
        await store.Add(person, ct);
        return person;
    }

    public async Task<RecognitionResult> Recognize(byte[] imageData, FaceBox face, CancellationToken ct = default)
    {
        var query = embedder.Embed(imageData, face);
        var hits = await store.FindNearest(query, options.CandidateCount, ct);

        if (hits.Count == 0)
            return RecognitionResult.NoMatch;

        var best = hits[0];
        if (best.Distance > options.MaxDistance)
            return RecognitionResult.NoMatch;

        return new RecognitionResult(best.Person.Name, best.Distance, best.Person.Id);
    }

    public Task<IReadOnlyList<Person>> GetAll(CancellationToken ct = default) => store.GetAll(ct);

    public Task<int> Forget(string name, CancellationToken ct = default) => store.RemoveByName(name, ct);
}
