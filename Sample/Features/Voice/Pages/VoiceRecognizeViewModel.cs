using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sample.Features.Voice;
using Sample.Features.Voice.Audio;
using Shiny;
using Shiny.VoiceIntelligence;

namespace Sample.Features.Voice.Pages;

// Records a short utterance on tap and reports the nearest enrolled speaker. Unlike face Recognize
// (continuous off the camera), audio recognition is button-driven — you can't passively sample a voice.
[ShellMap<VoiceRecognizePage>("VoiceRecognize", registerRoute: false)]
public partial class VoiceRecognizeViewModel(IVoiceIntelligence voice, VoiceRecorder recorder) : ObservableObject
{
    // Enroll and identify record for the SAME duration on purpose — see VoiceTuning.RecordFor.
    static readonly TimeSpan RecordFor = VoiceTuning.RecordFor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    public bool IsIdle => !this.IsBusy;

    [ObservableProperty]
    public partial string ResultText { get; set; } = "Tap and speak to identify the speaker.";

    /// <summary>
    /// Second line: the measured distance and the clip's level. "Unknown" alone can't distinguish a near
    /// miss (threshold too strict) from a random embedding (broken audio/features) — this can.
    /// </summary>
    [ObservableProperty]
    public partial string DiagnosticText { get; set; } = "";

    [RelayCommand]
    async Task Identify()
    {
        if (this.IsBusy)
            return;

        this.IsBusy = true;
        try
        {
            this.ResultText = "Listening… speak now.";
            var samples = await recorder.RecordAsync(RecordFor);

            var result = await voice.Recognize(samples);
            this.ResultText = result.IsMatch
                ? $"{result.PersonIdentifier}  ·  {result.Similarity:P0}"
                : "Unknown speaker";

            var diag = $"nearest distance {result.Distance:F3} (threshold {VoiceTuning.MaxDistance:F2}) · {Describe(samples)}";
            this.DiagnosticText = diag;
            Console.WriteLine($"[VoiceId] match={result.IsMatch} {diag}");
        }
        catch (FileNotFoundException)
        {
            this.ResultText = "Voice model missing — add ecapa.onnx (see README).";
        }
        catch (Exception ex)
        {
            this.ResultText = $"Error: {ex.Message}";
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    /// <summary>Peak/RMS and duration of the captured clip — a silent or clipped recording explains a lot.</summary>
    static string Describe(float[] samples)
    {
        if (samples.Length == 0)
            return "no audio captured";

        float peak = 0f, sumSq = 0f;
        foreach (var v in samples)
        {
            var a = MathF.Abs(v);
            if (a > peak) peak = a;
            sumSq += v * v;
        }
        var rms = MathF.Sqrt(sumSq / samples.Length);
        return $"{samples.Length / 16000f:F1}s peak {peak:F3} rms {rms:F4}";
    }
}
