namespace Shiny.VoiceIntelligence;

/// <summary>
/// Persists enrolled voiceprints and answers nearest-neighbor queries. This is the seam that decouples
/// <see cref="VoiceIntelligenceManager"/> from any particular database — the default implementation is
/// <c>DocumentDbVoiceStore</c> (Shiny.DocumentDb vector search), but a server could back it with pgvector,
/// a cloud vector service, or an in-memory store for tests. Mirrors face's <c>IFaceStore</c>.
/// </summary>
public interface IVoiceStore
{
    /// <summary>Store one enrolled <see cref="Speaker"/> (one utterance), including its embedding.</summary>
    Task Add(Speaker speaker, CancellationToken ct = default);

    /// <summary>
    /// Return the <paramref name="count"/> nearest enrolled voiceprints to <paramref name="embedding"/>,
    /// ordered nearest-first (smallest <see cref="VoiceMatch.Distance"/> first).
    /// </summary>
    Task<IReadOnlyList<VoiceMatch>> FindNearest(ReadOnlyMemory<float> embedding, int count, CancellationToken ct = default);

    /// <summary>All enrolled speakers, most-recently enrolled first. One entry per stored utterance.</summary>
    Task<IReadOnlyList<Speaker>> GetAll(CancellationToken ct = default);

    /// <summary>Delete every enrolled utterance for a given identity. Returns the number removed.</summary>
    Task<int> RemoveByPersonIdentifier(string personIdentifier, CancellationToken ct = default);

    /// <summary>
    /// Delete the given utterances (<see cref="Speaker.Id"/> document ids) — but only those that belong to
    /// <paramref name="personIdentifier"/>; ids of other identities are ignored. Returns the number removed.
    /// Mirrors face's <c>IFaceStore.RemoveShots</c>.
    /// </summary>
    /// <remarks>Default implementation throws so existing third-party stores stay source compatible.</remarks>
    Task<int> RemoveShots(string personIdentifier, IReadOnlyCollection<string> shotIds, CancellationToken ct = default)
        => throw new NotSupportedException($"{this.GetType().Name} does not implement {nameof(RemoveShots)}.");
}

/// <summary>A nearest-neighbor hit: an enrolled <see cref="Speaker"/> and its distance to the query.</summary>
/// <param name="Speaker">The enrolled document (identifier, document id).</param>
/// <param name="Distance">Distance to the query embedding (cosine: 0 = identical, lower is closer).</param>
public readonly record struct VoiceMatch(Speaker Speaker, float Distance);
