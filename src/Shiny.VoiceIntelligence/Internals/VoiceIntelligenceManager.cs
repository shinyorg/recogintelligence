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
    public async Task<Speaker> Enroll(string personIdentifier, float[] samples, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personIdentifier);
        ArgumentNullException.ThrowIfNull(samples);

        // Embedding (ONNX inference) is synchronous and CPU-bound, and the first embed also loads the model —
        // run it off the caller's thread so awaiting from a UI thread never blocks the UI. See also Recognize.
        var id = personIdentifier.Trim();
        var person = await Task.Run(() => new Speaker
        {
            Id = Guid.NewGuid().ToString("n"), // string ids must be explicit
            PersonIdentifier = id,
            Embedding = embedder.Embed(samples),
            EnrolledAt = DateTimeOffset.UtcNow
        }, ct);
        await store.Add(person, ct);
        return person;
    }

    /// <summary>
    /// The configured match threshold, reachable from a method whose own parameter is called
    /// <c>options</c> (which would otherwise shadow the primary-constructor one).
    /// </summary>
    float MaxDistance => options.MaxDistance;

    public VoiceEnrollmentSession CreateEnrollment(string personIdentifier, VoiceEnrollmentOptions? options = null)
        // The session's distance gates only mean anything relative to the threshold matching actually uses,
        // so the defaults come from this instance's configuration rather than from a constant.
        => new(personIdentifier, embedder, store, options ?? VoiceEnrollmentOptions.ForThreshold(this.MaxDistance));

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
            // A near miss and a total stranger are both "no match", but they mean completely different
            // things when tuning MaxDistance — so report how close the nearest actually got. The identifier stays
            // null, so IsMatch is still false and callers that only check IsMatch are unaffected.
            return new RecognitionResult(null, best.Distance, null);

        return new RecognitionResult(best.Speaker.PersonIdentifier, best.Distance, best.Speaker.Id);
    }

    public Task<IReadOnlyList<Speaker>> GetAll(CancellationToken ct = default) => store.GetAll(ct);

    public Task<int> Forget(string personIdentifier, CancellationToken ct = default) => store.RemoveByPersonIdentifier(personIdentifier, ct);
}
