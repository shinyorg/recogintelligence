namespace Shiny.FaceIntelligence;

/// <summary>The outcome of matching a probe face against the enrolled set.</summary>
/// <param name="PersonIdentifier">The matched identity, or null when nothing crossed the threshold.</param>
/// <param name="Distance">
/// Cosine distance to the nearest enrolled embedding (0 = identical, 2 = opposite). Lower is closer.
/// </param>
/// <param name="DocumentId">
/// Id of the nearest enrolled <see cref="Person"/> <i>document</i> — one stored shot, not the person. Use
/// <paramref name="PersonIdentifier"/> for identity.
/// </param>
public record RecognitionResult(string? PersonIdentifier, float Distance, string? DocumentId)
{
    /// <summary>True when an identity was matched within the configured threshold.</summary>
    public bool IsMatch => this.PersonIdentifier is not null;

    /// <summary>Cosine similarity (1 = identical), derived from <see cref="Distance"/>. Handy for UI.</summary>
    public float Similarity => 1f - this.Distance;

    /// <summary>An empty, no-match result.</summary>
    public static readonly RecognitionResult NoMatch = new(null, float.MaxValue, null);
}
