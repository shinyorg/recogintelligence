namespace Shiny.VoiceIntelligence;

/// <summary>
/// Measures a captured utterance: level, clipping, how much of it is actually speech, and a rough SNR.
/// Pure arithmetic over the sample buffer — no audio dependency, no model. The voice analogue of face's
/// <c>FrameQuality</c>, and used for the same purpose: reject a bad capture <i>before</i> it becomes a
/// stored template.
/// </summary>
/// <remarks>
/// Speech/silence is decided by frame energy, not by a VAD model. That is enough for the job here (is this
/// person talking for most of the window, and is there signal above the room?) and keeps core dependency-free.
/// It will call a loud steady noise "speech" — which is why <see cref="VoiceSampleMetrics.SnrDb"/> is
/// reported alongside rather than trusted alone.
/// </remarks>
public static class VoiceQuality
{
    const float SilenceFloor = 1e-9f;

    /// <summary>Frames shorter than this are pointless to analyze; 20 ms is the usual speech-analysis frame.</summary>
    const float FrameSeconds = 0.02f;

    /// <summary>How far above the noise floor a frame must sit to count as speech.</summary>
    const float SpeechOverNoiseDb = 6f;

    /// <summary>...and no further than this below the loudest speech, so a loud room can't swallow the gate.</summary>
    const float SpeechUnderPeakDb = 25f;

    /// <summary>Measure <paramref name="samples"/> (mono, [-1, 1]) captured at <paramref name="sampleRate"/> Hz.</summary>
    public static VoiceSampleMetrics Measure(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (samples.Length == 0 || sampleRate <= 0)
            return default;

        var seconds = samples.Length / (float)sampleRate;

        double sumSquares = 0;
        var peak = 0f;
        var clipped = 0;
        foreach (var s in samples)
        {
            sumSquares += (double)s * s;
            var abs = MathF.Abs(s);
            if (abs > peak)
                peak = abs;
            if (abs >= 0.99f)
                clipped++;
        }
        var rms = (float)Math.Sqrt(sumSquares / samples.Length);

        var frameSize = Math.Max(1, (int)(sampleRate * FrameSeconds));
        var frameCount = samples.Length / frameSize;
        if (frameCount < 2)
            // Too short to say anything about speech vs silence; report the levels and let the caller's
            // duration gate be the thing that rejects it.
            return new VoiceSampleMetrics(seconds, rms, rms, peak, clipped / (float)samples.Length, 0f, 0f);

        var frameRms = new float[frameCount];
        for (var f = 0; f < frameCount; f++)
        {
            double acc = 0;
            var frame = samples.Slice(f * frameSize, frameSize);
            foreach (var s in frame)
                acc += (double)s * s;
            frameRms[f] = (float)Math.Sqrt(acc / frameSize);
        }

        var sorted = (float[])frameRms.Clone();
        Array.Sort(sorted);
        var noise = Percentile(sorted, 0.10f);
        var speech = Percentile(sorted, 0.90f);

        var noiseDb = Db(noise);
        var speechDb = Db(speech);
        var thresholdDb = MathF.Max(noiseDb + SpeechOverNoiseDb, speechDb - SpeechUnderPeakDb);

        var speechFrames = 0;
        double speechEnergy = 0;
        for (var f = 0; f < frameCount; f++)
        {
            if (Db(frameRms[f]) < thresholdDb)
                continue;
            speechFrames++;
            speechEnergy += (double)frameRms[f] * frameRms[f];
        }

        var speechRms = speechFrames == 0 ? 0f : (float)Math.Sqrt(speechEnergy / speechFrames);
        return new VoiceSampleMetrics(
            Seconds: seconds,
            Rms: rms,
            SpeechRms: speechRms,
            Peak: peak,
            ClippedFraction: clipped / (float)samples.Length,
            SpeechSeconds: speechFrames * frameSize / (float)sampleRate,
            SnrDb: speechDb - noiseDb
        );
    }

    static float Percentile(float[] sorted, float p)
        => sorted[Math.Clamp((int)(p * (sorted.Length - 1)), 0, sorted.Length - 1)];

    static float Db(float amplitude) => 20f * MathF.Log10(MathF.Max(amplitude, SilenceFloor));
}
