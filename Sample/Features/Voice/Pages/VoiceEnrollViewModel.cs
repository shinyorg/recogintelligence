using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sample.Features.Voice.Audio;
using Shiny;
using Shiny.VoiceIntelligence;

namespace Sample.Features.Voice.Pages;

// Records a short utterance off an explicit tap, then embeds + stores it. The mic is the audio twin of
// the face Enroll camera; capture lifetime lives in VoiceRecorder, enrollment logic here.
[ShellMap<VoiceEnrollPage>("VoiceEnroll", registerRoute: false)]
public partial class VoiceEnrollViewModel(IVoiceIntelligence voice, VoiceRecorder recorder) : ObservableObject
{
    static readonly TimeSpan RecordFor = TimeSpan.FromSeconds(5);

    // The speaker model is text-INDEPENDENT — it captures how you sound, not what you say — so these are
    // just phonetically rich prompts (TIMIT/Harvard) that exercise many sounds for a stronger voiceprint.
    // We rotate through them so multiple enrollments cover varied phonemes, not the same words each time.
    static readonly string[] Prompts =
    [
        "She had your dark suit in greasy wash water all year.",
        "Don't ask me to carry an oily rag like that.",
        "The birch canoe slid on the smooth planks.",
        "Glue the sheet to the dark blue background.",
    ];

    int shotCount;

    [ObservableProperty]
    public partial string? Name { get; set; }

    [ObservableProperty]
    public partial string PromptText { get; set; } = Prompts[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    public bool IsIdle => !this.IsBusy;

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Enter a name, then tap Record and read the sentence aloud. Enroll 3–4 times for accuracy.";

    [RelayCommand]
    async Task Record()
    {
        var name = this.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            this.StatusText = "Enter a name first.";
            return;
        }
        if (this.IsBusy)
            return;

        this.IsBusy = true;
        try
        {
            this.StatusText = "Recording… read the sentence aloud now.";
            var samples = await recorder.RecordAsync(RecordFor);

            this.StatusText = "Enrolling…";
            await voice.Enroll(name, samples);
            this.shotCount++;

            // Advance to the next prompt so the next recording exercises different phonemes.
            this.PromptText = Prompts[this.shotCount % Prompts.Length];
            this.StatusText = $"Enrolled '{name}' — {this.shotCount} sample(s). Read the new sentence and record again for accuracy.";
        }
        catch (FileNotFoundException)
        {
            this.StatusText = "Voice model missing — add ecapa.onnx to Sample/Resources/Raw.";
        }
        catch (Exception ex)
        {
            this.StatusText = $"Enroll failed ({ex.GetType().Name}): {ex.Message}";
        }
        finally
        {
            this.IsBusy = false;
        }
    }
}
