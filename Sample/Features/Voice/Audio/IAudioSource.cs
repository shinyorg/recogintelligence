namespace Sample.Features.Voice.Audio;

/// <summary>
/// Platform microphone capture. The stream it returns is <b>float32, mono, little-endian</b> samples at
/// <see cref="SampleRate"/> Hz (the device's native input rate — <see cref="VoiceRecorder"/> resamples to
/// the 16 kHz the embedder expects). Vendored from Shiny.Audio (not published to NuGet); permission is
/// handled by <see cref="VoiceRecorder"/> via MAUI's built-in Microphone permission.
/// </summary>
public interface IAudioSource : IAsyncDisposable
{
    /// <summary>
    /// Sample rate (Hz) of the stream produced by <see cref="StartCaptureAsync"/>. Only meaningful once
    /// capture has started (the Apple input node reports its rate at start time).
    /// </summary>
    int SampleRate { get; }

    /// <summary>Start capturing; returns a stream of raw float32 / mono PCM samples.</summary>
    Task<Stream> StartCaptureAsync(CancellationToken cancellationToken = default);

    /// <summary>Stop capturing and release the microphone.</summary>
    Task StopCaptureAsync();
}
