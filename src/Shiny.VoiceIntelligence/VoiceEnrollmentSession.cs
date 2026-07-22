namespace Shiny.VoiceIntelligence;

/// <summary>
/// Guided voice enrollment: hand it one recording at a time and it decides whether the recording is good
/// enough to keep, how many more it needs, and when the voiceprint set is strong enough to stop. Nothing is
/// stored until it finishes, so an abandoned run leaves no half-enrolled speaker behind.
/// </summary>
/// <example>
/// <code language="csharp">
/// var session = voice.CreateEnrollment("Allan");
/// while (!session.IsComplete)
/// {
///     ShowPrompt(session.CurrentPrompt);                 // "The birch canoe slid on the smooth planks."
///     var samples = await recorder.RecordAsync(TimeSpan.FromSeconds(5));
///     var step = await session.Submit(samples);
///     ShowHint(step.Hint);                               // "" when accepted
/// }
/// var result = session.Result!;                          // stored; result.Cohesion is the quality number
/// </code>
/// </example>
/// <remarks>
/// <para>
/// <b>This is the voice counterpart of <c>FaceEnrollmentView</c>, with the gate inverted.</b> The face
/// wizard wants <i>spread</i> — a gallery of varied poses — so it rejects shots that look like ones it
/// already has. Voice wants the opposite: a speaker embedding is supposed to be the same whatever the
/// person says, so recordings that <i>disagree</i> are the suspect ones. Agreement is therefore both the
/// quality gate and the stop condition: keep recording until enough clips agree closely enough to leave
/// matching headroom.
/// </para>
/// <para>
/// <b>It lives in core, not in a UI control</b>, because this library never touches audio hardware — the
/// app records and hands over samples, exactly as with <see cref="IVoiceIntelligence.Enroll"/>. That also
/// makes the session usable server-side and testable without a microphone.
/// </para>
/// <para>
/// <b>What it cannot check:</b> that the person read the prompt (no speech-to-text), and that the voice is
/// live rather than a recording or a clone (no anti-spoofing anywhere in this stack yet). Agreement between
/// clips is evidence of a consistent capture, not of a genuine speaker.
/// </para>
/// <para>
/// Cohesion is measured within this session only. Re-running enrollment for a name that already has stored
/// utterances says nothing about how the new set relates to the old one.
/// </para>
/// </remarks>
public class VoiceEnrollmentSession
{
    readonly ISpeakerEmbedder embedder;
    readonly IVoiceStore store;
    readonly List<Candidate> accepted = [];

    // A rejected-as-inconsistent recording, held for one round so two clips that agree with each other can
    // out-vote a single bad first clip. See the rescue path in Submit.
    Candidate? heldOut;
    int attempts;

    /// <param name="name">Name to enroll under.</param>
    /// <param name="embedder">Turns samples into voiceprints; also defines the required sample rate.</param>
    /// <param name="store">Where accepted recordings are written when the session completes.</param>
    /// <param name="options">Prompts, sample counts and gates. See <see cref="VoiceEnrollmentOptions"/>.</param>
    public VoiceEnrollmentSession(string name, ISpeakerEmbedder embedder, IVoiceStore store, VoiceEnrollmentOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(embedder);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Prompts.Count == 0)
            throw new ArgumentException("At least one prompt is required.", nameof(options));
        if (options.MinSamples < 1 || options.MaxSamples < options.MinSamples)
            throw new ArgumentException("MaxSamples must be at least MinSamples, and MinSamples at least 1.", nameof(options));

        this.Name = name.Trim();
        this.embedder = embedder;
        this.store = store;
        this.Options = options;
    }

    /// <summary>The name being enrolled.</summary>
    public string Name { get; }

    /// <inheritdoc cref="VoiceEnrollmentOptions"/>
    public VoiceEnrollmentOptions Options { get; }

    /// <summary>
    /// The sentence to show for the next recording. Rotates every attempt (accepted or not) so nobody reads
    /// the same line twice in a row.
    /// </summary>
    public string CurrentPrompt => this.Options.Prompts[this.attempts % this.Options.Prompts.Count];

    /// <summary>How many recordings have been kept so far.</summary>
    public int AcceptedCount => this.accepted.Count;

    /// <summary>How many recordings have been submitted, kept or not.</summary>
    public int AttemptCount => this.attempts;

    /// <summary>
    /// Largest cosine distance between any two accepted recordings — how much they disagree. Zero until
    /// there are two. Lower is better; <see cref="VoiceEnrollmentOptions.MaxCohesionDistance"/> is the target.
    /// </summary>
    public float Cohesion
    {
        get
        {
            var worst = 0f;
            for (var i = 0; i < this.accepted.Count; i++)
                for (var j = i + 1; j < this.accepted.Count; j++)
                    worst = MathF.Max(worst, Distance(this.accepted[i].Embedding, this.accepted[j].Embedding));
            return worst;
        }
    }

    /// <summary>
    /// A floor on how many more recordings are wanted: zero once complete, otherwise at least one (more if
    /// the minimum count hasn't been reached). It is a floor because a recording that disagrees with the
    /// others extends the run.
    /// </summary>
    public int SamplesStillNeeded => this.IsComplete ? 0 : Math.Max(1, this.Options.MinSamples - this.accepted.Count);

    /// <summary>True once the session has finished and stored its recordings.</summary>
    public bool IsComplete => this.Result is not null;

    /// <summary>The finished enrollment, or null while still running.</summary>
    public VoiceEnrollmentResult? Result { get; private set; }

    /// <summary>
    /// Submit one recording: mono PCM in [-1, 1] at <see cref="ISpeakerEmbedder.SampleRate"/>, the same
    /// format <see cref="IVoiceIntelligence.Enroll"/> takes. Record all of them the same way and for the
    /// same duration you will use for recognition.
    /// </summary>
    /// <returns>
    /// Whether it was kept and why not if it wasn't; <see cref="VoiceEnrollmentStepResult.Result"/> is set
    /// on the call that completes the session.
    /// </returns>
    public async Task<VoiceEnrollmentStepResult> Submit(float[] samples, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (this.IsComplete)
            throw new InvalidOperationException($"This enrollment is finished. Call {nameof(this.Reset)}() to start another.");

        this.attempts++;

        var metrics = VoiceQuality.Measure(samples, this.embedder.SampleRate);
        var audioProblem = this.CheckAudio(metrics);
        if (audioProblem != VoiceEnrollmentRejection.None)
            return Reject(audioProblem, metrics, null);

        // Embedding is CPU-bound (and loads the model on the first call) — keep it off the caller's thread,
        // as VoiceIntelligenceManager does.
        var embedding = (await Task.Run(() => this.embedder.Embed(samples), ct).ConfigureAwait(false)).ToArray();
        var candidate = new Candidate(embedding, metrics);

        float? nearest = null;
        if (this.accepted.Count > 0)
        {
            nearest = this.accepted.Min(a => Distance(a.Embedding, embedding));
            var furthest = this.accepted.Max(a => Distance(a.Embedding, embedding));

            // Must agree with every recording kept so far, not just the closest one — otherwise the set
            // drifts one accepted clip at a time.
            if (furthest > this.Options.MaxOutlierDistance)
            {
                // Rescue: with a single recording kept, a disagreement is ambiguous — the new clip may be
                // bad, or the first one was and everything since has been measured against garbage. If two
                // consecutive rejects agree with each other, they out-vote the lone survivor.
                if (this.accepted.Count == 1 &&
                    this.heldOut is { } previous &&
                    Distance(previous.Embedding, embedding) <= this.Options.MaxCohesionDistance)
                {
                    this.accepted.Clear();
                    this.accepted.Add(previous);
                    this.heldOut = null;
                    nearest = Distance(previous.Embedding, embedding);
                }
                else
                {
                    this.heldOut = candidate;
                    return Reject(VoiceEnrollmentRejection.Inconsistent, metrics, nearest);
                }
            }
        }

        this.accepted.Add(candidate);
        this.heldOut = null;

        var result = await this.TryFinish(ct).ConfigureAwait(false);
        return new VoiceEnrollmentStepResult(true, VoiceEnrollmentRejection.None, String.Empty, metrics, nearest, result);
    }

    /// <summary>
    /// Discard everything and start over. Safe at any point — recordings accepted before completion were
    /// never written anywhere. Does not remove speakers stored by a completed session (use
    /// <see cref="IVoiceIntelligence.Forget"/> for that).
    /// </summary>
    public void Reset()
    {
        this.accepted.Clear();
        this.heldOut = null;
        this.attempts = 0;
        this.Result = null;
    }

    async Task<VoiceEnrollmentResult?> TryFinish(CancellationToken ct)
    {
        var enough = this.accepted.Count >= this.Options.MinSamples;
        var coherent = this.Cohesion <= this.Options.MaxCohesionDistance;

        if (enough && coherent)
            return await this.Store(true, ct).ConfigureAwait(false);

        if (this.accepted.Count < this.Options.MaxSamples)
            return null;   // keep asking

        // Out of attempts. Drop the recordings that disagree most with the rest — one loose clip poisons a
        // set that is otherwise fine — down to the minimum count, then store whatever that leaves.
        while (this.accepted.Count > this.Options.MinSamples && this.Cohesion > this.Options.MaxCohesionDistance)
            this.accepted.RemoveAt(this.WorstIndex());

        return await this.Store(this.Cohesion <= this.Options.MaxCohesionDistance, ct).ConfigureAwait(false);
    }

    async Task<VoiceEnrollmentResult> Store(bool confident, CancellationToken ct)
    {
        var speakers = new List<Speaker>(this.accepted.Count);
        foreach (var candidate in this.accepted)
        {
            // Written straight to the store rather than through IVoiceIntelligence.Enroll: the voiceprint
            // was already computed for the agreement checks, and re-embedding every clip at the finish line
            // would double the inference cost for an identical result.
            var speaker = new Speaker
            {
                Id = Guid.NewGuid().ToString("n"),
                Name = this.Name,
                Embedding = candidate.Embedding,
                EnrolledAt = DateTimeOffset.UtcNow
            };
            await this.store.Add(speaker, ct).ConfigureAwait(false);
            speakers.Add(speaker);
        }

        this.Result = new VoiceEnrollmentResult(this.Name, speakers, this.Cohesion, confident);
        return this.Result;
    }

    /// <summary>Index of the recording that disagrees most with the others (largest mean distance).</summary>
    int WorstIndex()
    {
        var worst = 0;
        var worstMean = -1f;
        for (var i = 0; i < this.accepted.Count; i++)
        {
            var sum = 0f;
            for (var j = 0; j < this.accepted.Count; j++)
            {
                if (i != j)
                    sum += Distance(this.accepted[i].Embedding, this.accepted[j].Embedding);
            }

            var mean = sum / (this.accepted.Count - 1);
            if (mean > worstMean)
            {
                worstMean = mean;
                worst = i;
            }
        }
        return worst;
    }

    VoiceEnrollmentRejection CheckAudio(VoiceSampleMetrics metrics)
    {
        var o = this.Options;
        if (o.MinSpeechSeconds > 0 && metrics.Seconds < o.MinSpeechSeconds)
            return VoiceEnrollmentRejection.TooShort;
        if (o.MaxClippedFraction > 0 && metrics.ClippedFraction > o.MaxClippedFraction)
            return VoiceEnrollmentRejection.Clipped;
        if (o.MinSpeechSeconds > 0 && metrics.SpeechSeconds < o.MinSpeechSeconds)
            return VoiceEnrollmentRejection.TooLittleSpeech;
        if (o.MinSpeechLevel > 0 && metrics.SpeechRms < o.MinSpeechLevel)
            return VoiceEnrollmentRejection.TooQuiet;
        if (o.MinSnrDb >= 0 && metrics.SnrDb < o.MinSnrDb)
            return VoiceEnrollmentRejection.TooNoisy;

        return VoiceEnrollmentRejection.None;
    }

    static VoiceEnrollmentStepResult Reject(VoiceEnrollmentRejection reason, VoiceSampleMetrics metrics, float? distance)
        => new(false, reason, Hint(reason), metrics, distance, null);

    /// <summary>Human-readable guidance for a rejection, ready to put on screen.</summary>
    public static string Hint(VoiceEnrollmentRejection reason) => reason switch
    {
        VoiceEnrollmentRejection.TooShort => "That recording was too short — keep reading until it stops.",
        VoiceEnrollmentRejection.TooLittleSpeech => "Mostly silence — start speaking as soon as recording begins and keep going.",
        VoiceEnrollmentRejection.TooQuiet => "Too quiet — speak up, or hold the mic closer.",
        VoiceEnrollmentRejection.Clipped => "Too loud — move the mic further away.",
        VoiceEnrollmentRejection.TooNoisy => "Too much background noise — try somewhere quieter.",
        VoiceEnrollmentRejection.Inconsistent => "That didn't sound like your other recordings — same room, same distance, and make sure it's the same person.",
        _ => String.Empty
    };

    /// <summary>Cosine distance between two L2-normalized voiceprints (0 = identical).</summary>
    static float Distance(float[] a, float[] b)
    {
        var dot = 0f;
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
            dot += a[i] * b[i];
        return 1f - dot;
    }

    // Only the voiceprint is kept — the sample buffer has done its job by this point, and holding several
    // seconds of audio per accepted recording for the length of the session buys nothing.
    sealed record Candidate(float[] Embedding, VoiceSampleMetrics Metrics);
}
