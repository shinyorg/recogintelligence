using Sample.Features.Voice.Audio;
using Sample.Infrastructure;
using Shiny;
using Shiny.VoiceIntelligence;
using Shiny.VoiceIntelligence.DocumentDb.Sqlite;
using Shiny.VoiceIntelligence.Onnx;

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
            // See VoiceTuning.MaxDistance for why this moved off the synthetic-audio value of 0.4.
            voice.Options.MaxDistance = VoiceTuning.MaxDistance;

            // The bundled model (ecapa.onnx) is WeSpeaker CAM++ — a FEATURE-input model consuming 80-bin
            // kaldi fbank [1,T,80], not a raw waveform. UseOnnxEmbedder detects that from the model itself
            // and runs KaldiFbank first; Dimensions must be the model's real 512 (it sizes the vector store
            // before the model loads). Loaded lazily on first enroll/recognize.
            voice.UseOnnxEmbedder(o =>
            {
                o.ModelBytesProvider = () => BundledAssets.LoadBundledModel("ecapa.onnx");
                o.Dimensions = 512;
                o.SampleRate = 16000;
            });

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
