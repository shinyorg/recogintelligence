namespace Shiny.VoiceIntelligence;

/// <summary>
/// Default <see cref="IVoiceIntelligence"/>: embeds voices with <see cref="ISpeakerEmbedder"/> and
/// stores/matches them via an <see cref="IVoiceStore"/> (nearest-neighbor vector search), thresholded by
/// <see cref="VoiceIntelligenceOptions.MaxDistance"/>. The voice analogue of <c>FaceIntelligenceManager</c>.
/// </summary>
public class VoiceIntelligenceManager(
    IVoiceStore store,
    ISpeakerEmbedder embedder,
    VoiceIntelligenceOptions options
) : IVoiceIntelligence
{
    public async Task<Speaker> Enroll(string name, float[] samples, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(samples);

        // Embedding (ONNX inference) is synchronous and CPU-bound, and the first embed also loads the model —
        // run it off the caller's thread so awaiting from a UI thread never blocks the UI. See also Recognize.
        var name2 = name.Trim();
        var person = await Task.Run(() => new Speaker
        {
            Id = Guid.NewGuid().ToString("n"), // string ids must be explicit
            Name = name2,
            Embedding = embedder.Embed(samples),
            EnrolledAt = DateTimeOffset.UtcNow
        }, ct);
        await store.Add(person, ct);
        return person;
    }

    public async Task<RecognitionResult> Recognize(float[] samples, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(samples);

        // Embed() is synchronous and CPU-bound (and lazily loads the model on first call) — offload it so a
        // caller awaiting on the UI thread keeps a responsive UI.
        var query = await Task.Run(() => embedder.Embed(samples), ct);
        var hits = await store.FindNearest(query, options.CandidateCount, ct);

        if (hits.Count == 0)
            return RecognitionResult.NoMatch;

        var best = hits[0];
        if (best.Distance > options.MaxDistance)
            return RecognitionResult.NoMatch;

        return new RecognitionResult(best.Speaker.Name, best.Distance, best.Speaker.Id);
    }

    public Task<IReadOnlyList<Speaker>> GetAll(CancellationToken ct = default) => store.GetAll(ct);

    public Task<int> Forget(string name, CancellationToken ct = default) => store.RemoveByName(name, ct);
}
