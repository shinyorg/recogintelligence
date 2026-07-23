using Shiny.VoiceIntelligence.Maui;

namespace Sample.Features.Voice.Audio;

/// <summary>
/// The single seam the voice pages record through: request the mic, capture for a fixed duration, and
/// return the utterance as the mono <c>float[]</c> at 16 kHz that <c>IVoiceIntelligence</c> expects.
/// Owns permission (MAUI Microphone), capture lifetime, float32→resample→16 kHz normalization.
/// </summary>
public class VoiceRecorder(IAudioSource source) : IVoiceRecorder
{
    const int TargetSampleRate = 16000;

    /// <summary>Record for <paramref name="duration"/> and return mono 16 kHz samples in [-1, 1].</summary>
    public Task<float[]> RecordAsync(TimeSpan duration, CancellationToken ct = default)
        => this.RecordAsync(duration, null, ct);

    /// <inheritdoc cref="IVoiceRecorder.RecordAsync(TimeSpan, IProgress{float}, CancellationToken)"/>
    public async Task<float[]> RecordAsync(TimeSpan duration, IProgress<float>? level, CancellationToken ct = default)
    {
        var status = await Permissions.RequestAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted)
            throw new InvalidOperationException("Microphone permission denied.");

        var stream = await source.StartCaptureAsync(ct);
        using var ms = new MemoryStream();
        try
        {
            // The duration is the stop signal — capture is continuous, so cancel the read loop when it elapses.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(duration);

            var buffer = new byte[8192];
            try
            {
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, 0, buffer.Length, timeoutCts.Token);
                    if (read <= 0)
                        break;
                    ms.Write(buffer, 0, read);
                    // Report the level from the CAPTURED chunk, before resampling — the meter should show
                    // what the mic is hearing right now, not what the pipeline produces at the end.
                    level?.Report(ChunkRms(buffer, read));
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // Expected: the recording window elapsed.
            }
        }
        finally
        {
            await source.StopCaptureAsync();
        }

        var captured = ToFloatSamples(ms.GetBuffer(), (int)ms.Length);
        var resampled = Resample(captured, source.SampleRate, TargetSampleRate);
        Console.WriteLine(
            $"[Audio] captured {captured.Length} @ {source.SampleRate} Hz " +
            $"({captured.Length / (float)source.SampleRate:F2}s) -> {resampled.Length} @ {TargetSampleRate} Hz");
        DumpWav(resampled);
        return resampled;
    }

    /// <summary>
    /// Write the final 16 kHz buffer to the app container so a recording can be pulled off-device
    /// (<c>xcrun devicectl device copy from --domain-type appDataContainer</c>) and inspected against the
    /// model directly. Diagnosing a speaker-matching problem from a distance number alone is guesswork;
    /// having the actual audio is not.
    /// </summary>
    [System.Diagnostics.Conditional("DEBUG")]
    static void DumpWav(float[] samples)
    {
        try
        {
            var dir = Path.Combine(FileSystem.AppDataDirectory, "recordings");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"rec-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.wav");

            using var fs = File.Create(path);
            using var w = new BinaryWriter(fs);
            var dataBytes = samples.Length * 2;
            w.Write("RIFF"u8.ToArray());
            w.Write(36 + dataBytes);
            w.Write("WAVE"u8.ToArray());
            w.Write("fmt "u8.ToArray());
            w.Write(16);
            w.Write((short)1);                       // PCM
            w.Write((short)1);                       // mono
            w.Write(TargetSampleRate);
            w.Write(TargetSampleRate * 2);           // byte rate
            w.Write((short)2);                       // block align
            w.Write((short)16);                      // bits
            w.Write("data"u8.ToArray());
            w.Write(dataBytes);
            foreach (var s in samples)
                w.Write((short)Math.Clamp((int)MathF.Round(s * 32767f), short.MinValue, short.MaxValue));

            Console.WriteLine($"[Audio] wrote {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Audio] dump failed: {ex.Message}");
        }
    }

    /// <summary>Reinterpret the captured little-endian float32 bytes as a float sample array.</summary>
    /// <summary>RMS of one captured chunk (native float32 mono), for the VU meter.</summary>
    static float ChunkRms(byte[] bytes, int count)
    {
        var samples = count / sizeof(float);
        if (samples == 0)
            return 0f;

        double sum = 0;
        for (var i = 0; i < samples; i++)
        {
            var v = BitConverter.ToSingle(bytes, i * sizeof(float));
            sum += (double)v * v;
        }
        return (float)Math.Sqrt(sum / samples);
    }

    static float[] ToFloatSamples(byte[] bytes, int byteCount)
    {
        var count = byteCount / sizeof(float);
        var samples = new float[count];
        Buffer.BlockCopy(bytes, 0, samples, 0, count * sizeof(float));
        return samples;
    }

    /// <summary>
    /// Band-limited (windowed-sinc) resample. A no-op when the rates already match (e.g. Android's 16 kHz).
    /// </summary>
    /// <remarks>
    /// This used to be linear interpolation, which is <b>not</b> a usable decimator: iPhone mics run at
    /// 48 kHz, so reaching 16 kHz throws away two of every three samples, and everything above 8 kHz folds
    /// back into the speech band as aliasing. Fricatives (/s/, /sh/, /f/) carry most of their energy up
    /// there, so the fold-back lands right on top of the band the fbank front end measures — and because
    /// aliasing is signal-dependent, two recordings of the same person are corrupted *differently*, which
    /// is what pushes their embeddings apart. The anti-alias low-pass is folded into the sinc kernel here,
    /// so filtering and rate conversion happen in one pass.
    /// </remarks>
    static float[] Resample(float[] input, int sourceRate, int targetRate)
    {
        if (sourceRate == targetRate || input.Length == 0)
            return input;

        var ratio = (double)targetRate / sourceRate;

        // Cut off just under the lower of the two Nyquist limits; when downsampling that IS the anti-alias filter.
        var cutoff = Math.Min(1.0, ratio) * 0.95;
        const int zeroCrossings = 24;              // kernel width; more = sharper transition, more cost
        var half = zeroCrossings / cutoff;         // kernel half-width in input samples

        var outLength = (int)(input.Length * ratio);
        var output = new float[outLength];

        for (var i = 0; i < outLength; i++)
        {
            var center = i / ratio;
            var lo = (int)Math.Ceiling(center - half);
            var hi = (int)Math.Floor(center + half);

            double acc = 0;
            for (var j = lo; j <= hi; j++)
            {
                if (j < 0 || j >= input.Length)
                    continue;

                var d = j - center;
                var x = d * cutoff;
                var sinc = Math.Abs(x) < 1e-9 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
                var window = 0.5 * (1.0 + Math.Cos(Math.PI * d / half));   // Hann taper
                acc += input[j] * sinc * window;
            }

            output[i] = (float)(acc * cutoff);
        }
        return output;
    }
}
