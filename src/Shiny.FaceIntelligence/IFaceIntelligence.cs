namespace Shiny.FaceIntelligence;

/// <summary>
/// The high-level face pipeline: enroll named faces and recognize unknown ones. Wraps the
/// <see cref="IFaceEmbedder"/> (image → vector) and the <see cref="IFaceStore"/> (vector → nearest name).
/// </summary>
public interface IFaceIntelligence
{
    /// <summary>
    /// Embed the face at <paramref name="face"/> in <paramref name="imageData"/> and store it under
    /// <paramref name="name"/>. Call several times per person (different angles/lighting) to strengthen
    /// recognition. Returns the stored <see cref="Person"/> document.
    /// </summary>
    Task<Person> Enroll(string name, byte[] imageData, FaceBox face, CancellationToken ct = default);

    /// <summary>
    /// Embed the face and return the nearest enrolled name within the configured distance threshold,
    /// or <see cref="RecognitionResult.NoMatch"/> when nothing is close enough.
    /// </summary>
    Task<RecognitionResult> Recognize(byte[] imageData, FaceBox face, CancellationToken ct = default);

    /// <summary>All enrolled people, most-recent first. One entry per stored shot.</summary>
    Task<IReadOnlyList<Person>> GetAll(CancellationToken ct = default);

    /// <summary>Delete every enrolled shot for a given name. Returns the number removed.</summary>
    Task<int> Forget(string name, CancellationToken ct = default);
}
