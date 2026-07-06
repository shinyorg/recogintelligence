namespace Sample.Features.Voice.Audio;

/// <summary>
/// The single seam the voice pages record through: request the mic, capture for a fixed duration, and
/// return the utterance as the mono <c>float[]</c> at 16 kHz that <c>IVoiceIntelligence</c> expects.
/// Owns permission (MAUI Microphone), capture lifetime, float32→resample→16 kHz normalization.
/// </summary>
public class VoiceRecorder(IAudioSource source)
{
    const int TargetSampleRate = 16000;

    /// <summary>Record for <paramref name="duration"/> and return mono 16 kHz samples in [-1, 1].</summary>
    public async Task<float[]> RecordAsync(TimeSpan duration, CancellationToken ct = default)
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
        return Resample(captured, source.SampleRate, TargetSampleRate);
    }

    /// <summary>Reinterpret the captured little-endian float32 bytes as a float sample array.</summary>
    static float[] ToFloatSamples(byte[] bytes, int byteCount)
    {
        var count = byteCount / sizeof(float);
        var samples = new float[count];
        Buffer.BlockCopy(bytes, 0, samples, 0, count * sizeof(float));
        return samples;
    }

    /// <summary>Linear-interpolation resample. A no-op when the rates already match (e.g. Android's 16 kHz).</summary>
    static float[] Resample(float[] input, int sourceRate, int targetRate)
    {
        if (sourceRate == targetRate || input.Length == 0)
            return input;

        var ratio = (double)targetRate / sourceRate;
        var outLength = (int)(input.Length * ratio);
        var output = new float[outLength];
        for (var i = 0; i < outLength; i++)
        {
            var srcPos = i / ratio;
            var idx = (int)srcPos;
            var frac = (float)(srcPos - idx);
            var a = input[idx];
            var b = idx + 1 < input.Length ? input[idx + 1] : a;
            output[i] = a + (b - a) * frac;
        }
        return output;
    }
}
