namespace Sample.Features.Voice.Audio;

/// <summary>
/// Placeholder audio source for platforms without a wired capture impl (MacCatalyst/Windows). Throws on
/// use so the voice pages report "not supported" rather than failing DI resolution at page load.
/// </summary>
public sealed class NullAudioSource : IAudioSource
{
    public int SampleRate => 16000;

    public Task<Stream> StartCaptureAsync(CancellationToken cancellationToken = default)
        => throw new PlatformNotSupportedException("Microphone capture isn't wired for this platform.");

    public Task StopCaptureAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
