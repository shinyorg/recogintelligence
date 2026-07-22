namespace Shiny.FaceIntelligence;

/// <summary>
/// Default <see cref="IFaceIntelligence"/>: embeds faces with <see cref="IFaceEmbedder"/> and stores/matches
/// them via an <see cref="IFaceStore"/> (nearest-neighbor vector search), thresholded by
/// <see cref="FaceIntelligenceOptions.MaxDistance"/>. When an <see cref="IFaceDetector"/> is registered, the
/// no-box <see cref="Enroll(string, byte[], bool, CancellationToken)"/>/<see cref="Recognize(byte[], CancellationToken)"/>
/// overloads locate the face themselves and enforce the detection gates in <see cref="FaceIntelligenceOptions"/>.
/// </summary>
public class FaceIntelligenceManager(
    IFaceStore store,
    IFaceEmbedder embedder,
    FaceIntelligenceOptions options,
    IEnumerable<IFaceDetector> detectors
) : IFaceIntelligence
{
    // Optional: box-based Enroll/Recognize work without a detector; the no-box overloads require one.
    readonly IFaceDetector? detector = detectors.FirstOrDefault();

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

    public async Task<Person> Enroll(string name, byte[] imageData, bool allowDuplicate = false, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var name2 = name.Trim();

        // Detect (gated) + embed + thumbnail are all synchronous/CPU-bound — offload the whole block.
        var (embedding, thumbnail) = await Task.Run(() =>
        {
            var best = this.RequireFace(imageData, enforceSingle: options.RejectMultipleFaces, enforceSize: true);
            return (embedder.Embed(imageData, best.Box), FaceImaging.EncodeThumbnail(imageData, best.Box));
        }, ct);

        // Duplicate/mismatch gate: if this face already looks like someone else, don't silently enroll it
        // under a new name. (FindNearest is async, so it stays outside the Task.Run above.)
        if (!allowDuplicate && options.GateEnrollmentOnRecognition)
        {
            var hits = await store.FindNearest(embedding, options.CandidateCount, ct);
            if (hits.Count > 0 &&
                hits[0].Distance <= options.MaxDistance &&
                !String.Equals(hits[0].Person.Name, name2, StringComparison.OrdinalIgnoreCase))
            {
                throw new FaceEnrollmentConflictException(
                    new RecognitionResult(hits[0].Person.Name, hits[0].Distance, hits[0].Person.Id));
            }
        }

        var person = new Person
        {
            Id = Guid.NewGuid().ToString("n"),
            Name = name2,
            Embedding = embedding,
            Thumbnail = thumbnail,
            EnrolledAt = DateTimeOffset.UtcNow
        };
        await store.Add(person, ct);
        return person;
    }

    public async Task<RecognitionResult> Recognize(byte[] imageData, FaceBox face, CancellationToken ct = default)
    {
        // Embed() is synchronous and CPU-bound (and lazily loads the model on first call) — offload it so a
        // caller awaiting on the UI thread keeps a responsive UI / live camera preview.
        var query = await Task.Run(() => embedder.Embed(imageData, face), ct);
        return await this.Match(query, ct);
    }

    public async Task<RecognitionResult> Recognize(byte[] imageData, CancellationToken ct = default)
    {
        // Recognition takes the single most-confident face; multiple faces aren't an error here (unlike enroll).
        var query = await Task.Run(() =>
        {
            var best = this.RequireFace(imageData, enforceSingle: false, enforceSize: false);
            return embedder.Embed(imageData, best.Box);
        }, ct);
        return await this.Match(query, ct);
    }

    public Task<IReadOnlyList<Person>> GetAll(CancellationToken ct = default) => store.GetAll(ct);

    public Task<int> Forget(string name, CancellationToken ct = default) => store.RemoveByName(name, ct);

    async Task<RecognitionResult> Match(ReadOnlyMemory<float> query, CancellationToken ct)
    {
        var hits = await store.FindNearest(query, options.CandidateCount, ct);
        if (hits.Count == 0)
            return RecognitionResult.NoMatch;

        var best = hits[0];
        if (best.Distance > options.MaxDistance)
            // A near miss and a total stranger are both "no match", but they mean completely different
            // things when tuning MaxDistance — so report how close the nearest actually got. Name stays
            // null, so IsMatch is still false and callers that only check IsMatch are unaffected.
            return new RecognitionResult(null, best.Distance, null);

        return new RecognitionResult(best.Person.Name, best.Distance, best.Person.Id);
    }

    /// <summary>
    /// Run the detector and apply the detection gates, returning the accepted face or throwing a typed
    /// <see cref="FaceDetectionException"/>. <paramref name="enforceSingle"/> rejects multiple faces;
    /// <paramref name="enforceSize"/> rejects a face that's too small a fraction of the frame.
    /// </summary>
    DetectedFace RequireFace(byte[] imageData, bool enforceSingle, bool enforceSize)
    {
        var d = this.detector ?? throw new InvalidOperationException(
            "No face detector registered. Inside AddFaceIntelligence(...), call UseOnnxDetector(...) " +
            "(Shiny.FaceIntelligence.Onnx) or UseDetector(...) — or use the Enroll/Recognize overload that " +
            "takes an explicit FaceBox.");

        var faces = d.Detect(imageData);
        if (faces.Count == 0)
            throw new FaceDetectionException(FaceDetectionError.NoFace,
                "No face detected. Move into the frame and face the camera.");

        var qualified = faces
            .Where(f => f.Confidence >= options.MinDetectionConfidence)
            .OrderByDescending(f => f.Confidence)
            .ToList();

        if (qualified.Count == 0)
            throw new FaceDetectionException(FaceDetectionError.LowConfidence,
                "A face was detected but the confidence was too low. Improve lighting and face the camera.");

        if (enforceSingle && qualified.Count > 1)
            throw new FaceDetectionException(FaceDetectionError.MultipleFaces,
                $"{qualified.Count} faces detected. Only one person should be in the frame.");

        var best = qualified[0];

        if (enforceSize && options.MinFaceSizeFraction > 0f)
        {
            var (w, h) = FaceImaging.GetDimensions(imageData);
            var shorter = Math.Min(w, h);
            if (shorter > 0 && Math.Min(best.Box.Width, best.Box.Height) < shorter * options.MinFaceSizeFraction)
                throw new FaceDetectionException(FaceDetectionError.TooSmall,
                    "The face is too small/far away. Move closer to the camera and try again.");
        }

        return best;
    }
}
