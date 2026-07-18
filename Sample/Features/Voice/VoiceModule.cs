using Sample.Features.Voice.Audio;
using Sample.Infrastructure;
using Shiny;
using Shiny.VoiceIntelligence;
using Shiny.VoiceIntelligence.DocumentDb.Sqlite;

namespace Sample.Features.Voice;

/// <summary>
/// Registers the voice (speaker) intelligence pipeline — fbank speaker embedder + sqlite-vec store —
/// plus the platform microphone capture (<see cref="IAudioSource"/>) the pages record with.
/// </summary>
public class VoiceModule : IMauiModule
{
    public void Add(MauiAppBuilder builder)
    {
        var dataDir = FileSystem.AppDataDirectory;
        builder.Services.AddVoiceIntelligence(voice =>
        {
            // Starting threshold for the bundled WeSpeaker CAM++ model. Synthetic-speaker validation put
            // same-speaker cosine distance ~0.12-0.16 and different-speaker ~0.36-0.64; 0.4 sits between.
            // Still tune against real FAR/FRR on-device before relying on it.
            voice.Options.MaxDistance = 0.4f;

            // The bundled model (ecapa.onnx = WeSpeaker CAM++) is a FEATURE-input model: it consumes
            // 80-bin kaldi fbank [1,T,80], not a raw waveform. So we can't use the core UseOnnxEmbedder
            // (which feeds [1,samples]); FbankSpeakerEmbedder computes fbank first. 512-d output, 16 kHz.
            // Loaded lazily on first enroll/recognize; a missing model surfaces there, not at startup.
            voice.UseEmbedder(_ => new FbankSpeakerEmbedder(
                () => BundledAssets.LoadBundledModel("ecapa.onnx"),
                dimensions: 512,
                sampleRate: 16000));

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
