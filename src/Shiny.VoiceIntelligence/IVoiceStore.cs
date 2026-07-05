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

    /// <summary>Delete every enrolled utterance for a given name. Returns the number removed.</summary>
    Task<int> RemoveByName(string name, CancellationToken ct = default);
}

/// <summary>A nearest-neighbor hit: an enrolled <see cref="Speaker"/> and its distance to the query.</summary>
/// <param name="Speaker">The enrolled document (name, id).</param>
/// <param name="Distance">Distance to the query embedding (cosine: 0 = identical, lower is closer).</param>
public readonly record struct VoiceMatch(Speaker Speaker, float Distance);
