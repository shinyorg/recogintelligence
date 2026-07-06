using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sample.Features.Voice.Audio;

public static class AudioCaptureRegistration
{
    /// <summary>Registers the platform <see cref="IAudioSource"/> + the <see cref="VoiceRecorder"/> seam.</summary>
    public static IServiceCollection AddAudioCapture(this IServiceCollection services)
    {
#if IOS
        services.TryAddSingleton<IAudioSource, AppleAudioSource>();
#elif ANDROID
        services.TryAddSingleton<IAudioSource, AndroidAudioSource>();
#else
        // MacCatalyst/Windows: no capture wired here (the MAUI heads are iOS + Android). The stub lets
        // DI resolve so the voice pages surface a friendly "not supported" message instead of crashing.
        services.TryAddSingleton<IAudioSource, NullAudioSource>();
#endif
        services.TryAddSingleton<VoiceRecorder>();
        return services;
    }
}
