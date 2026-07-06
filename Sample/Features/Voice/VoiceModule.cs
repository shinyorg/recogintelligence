using Sample.Features.Voice.Audio;
using Sample.Infrastructure;
using Shiny;
using Shiny.VoiceIntelligence;
using Shiny.VoiceIntelligence.Onnx;
using Shiny.VoiceIntelligence.DocumentDb.Sqlite;

namespace Sample.Features.Voice;

/// <summary>
/// Registers the voice (speaker) intelligence pipeline — ONNX ECAPA embedder + sqlite-vec store —
/// plus the platform microphone capture (<see cref="IAudioSource"/>) the pages record with.
/// </summary>
public class VoiceModule : IMauiModule
{
    public void Add(MauiAppBuilder builder)
    {
        var dataDir = FileSystem.AppDataDirectory;
        builder.Services.AddVoiceIntelligence(voice =>
        {
            // Speaker-embedding distributions vary far more by model/channel than face does — this
            // permissive default MUST be tuned against the real ECAPA export before it means anything.
            voice.Options.MaxDistance = 0.7f;

            // ECAPA-TDNN model ships as a Resources/Raw asset (ecapa.onnx), loaded lazily on first
            // enroll/recognize. A missing model surfaces there (handled by the pages), not at startup.
            voice.UseOnnxEmbedder(o => o.ModelBytesProvider = () => BundledAssets.LoadBundledModel("ecapa.onnx"));

            voice.UseSqliteStore(o =>
            {
                o.ConnectionString = $"Data Source={Path.Combine(dataDir, "voices.db")}";
            });
        });

        // Microphone capture (mono 16 kHz PCM) + the VoiceRecorder seam the pages use.
        builder.Services.AddAudioCapture();
    }

    public void Use(IPlatformApplication app) { }
}
