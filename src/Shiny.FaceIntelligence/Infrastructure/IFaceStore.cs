namespace Shiny.FaceIntelligence;

/// <summary>
/// Persists enrolled face embeddings and answers nearest-neighbor queries. This is the seam that
/// decouples <see cref="FaceIntelligenceManager"/> from any particular database — the default implementation
/// is <c>DocumentDbFaceStore</c> (Shiny.DocumentDb vector search), but a server could back it with
/// pgvector, a cloud vector service, or an in-memory store for tests.
/// </summary>
public interface IFaceStore
{
    /// <summary>Store one enrolled <see cref="Person"/> (one shot), including its embedding.</summary>
    Task Add(Person person, CancellationToken ct = default);

    /// <summary>
    /// Return the <paramref name="count"/> nearest enrolled faces to <paramref name="embedding"/>,
    /// ordered nearest-first (smallest <see cref="FaceMatch.Distance"/> first).
    /// </summary>
    Task<IReadOnlyList<FaceMatch>> FindNearest(ReadOnlyMemory<float> embedding, int count, CancellationToken ct = default);

    /// <summary>All enrolled people, most-recently enrolled first. One entry per stored shot.</summary>
    Task<IReadOnlyList<Person>> GetAll(CancellationToken ct = default);

    /// <summary>Delete every enrolled shot for a given identity. Returns the number removed.</summary>
    Task<int> RemoveByPersonIdentifier(string personIdentifier, CancellationToken ct = default);
}

/// <summary>A nearest-neighbor hit: an enrolled <see cref="Person"/> and its distance to the query.</summary>
/// <param name="Person">The enrolled document (with identifier, document id, thumbnail).</param>
/// <param name="Distance">Distance to the query embedding (cosine: 0 = identical, lower is closer).</param>
public readonly record struct FaceMatch(Person Person, float Distance);
