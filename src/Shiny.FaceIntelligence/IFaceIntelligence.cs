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
    /// Detect the face in <paramref name="imageData"/> with the registered <see cref="IFaceDetector"/>, then
    /// embed and store it under <paramref name="name"/>. Use this when you have a raw still and no face box
    /// (e.g. a single camera capture, no frame analyzer). Enforces the detection gates in
    /// <see cref="FaceIntelligenceOptions"/>:
    /// <list type="bullet">
    /// <item>throws <see cref="FaceDetectionException"/> for no face, low confidence, multiple faces, or a face too small;</item>
    /// <item>throws <see cref="FaceEnrollmentConflictException"/> if the face already matches a different enrolled name
    /// (unless <paramref name="allowDuplicate"/> is true).</item>
    /// </list>
    /// Requires a detector to be registered (<c>UseOnnxDetector</c>/<c>UseDetector</c>), else throws
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    Task<Person> Enroll(string name, byte[] imageData, bool allowDuplicate = false, CancellationToken ct = default);

    /// <summary>
    /// Embed the face and return the nearest enrolled name within the configured distance threshold,
    /// or <see cref="RecognitionResult.NoMatch"/> when nothing is close enough.
    /// </summary>
    Task<RecognitionResult> Recognize(byte[] imageData, FaceBox face, CancellationToken ct = default);

    /// <summary>
    /// Detect the face in <paramref name="imageData"/> with the registered <see cref="IFaceDetector"/>, then
    /// recognize it. Picks the most-confident face when several are present. Throws
    /// <see cref="FaceDetectionException"/> when no face (or none above the confidence threshold) is found.
    /// Requires a detector to be registered.
    /// </summary>
    Task<RecognitionResult> Recognize(byte[] imageData, CancellationToken ct = default);

    /// <summary>All enrolled people, most-recent first. One entry per stored shot.</summary>
    Task<IReadOnlyList<Person>> GetAll(CancellationToken ct = default);

    /// <summary>Delete every enrolled shot for a given name. Returns the number removed.</summary>
    Task<int> Forget(string name, CancellationToken ct = default);
}
