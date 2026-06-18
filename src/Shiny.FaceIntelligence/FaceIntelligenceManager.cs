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

        // Embedding (ONNX inference) and thumbnail encoding (image decode) are synchronous and CPU-bound,
        // and the first embed also loads the model — run them off the caller's thread so awaiting from a UI
        // thread never blocks the UI. See also Recognize.
        var name2 = name.Trim();
        var person = await Task.Run(() => new Person
        {
            Id = Guid.NewGuid().ToString("n"), // string ids must be explicit
            Name = name2,
            Embedding = embedder.Embed(imageData, face),
            Thumbnail = FaceImaging.EncodeThumbnail(imageData, face),
            EnrolledAt = DateTimeOffset.UtcNow
        }, ct);
        await store.Add(person, ct);
        return person;
    }

    public async Task<RecognitionResult> Recognize(byte[] imageData, FaceBox face, CancellationToken ct = default)
    {
        // Embed() is synchronous and CPU-bound (and lazily loads the model on first call) — offload it so a
        // caller awaiting on the UI thread keeps a responsive UI / live camera preview.
        var query = await Task.Run(() => embedder.Embed(imageData, face), ct);
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
