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
    static readonly TimeSpan RecordFor = TimeSpan.FromSeconds(4);
    int shotCount;

    [ObservableProperty]
    public partial string? Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    public bool IsIdle => !this.IsBusy;

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Enter a name, then record a few short utterances from a natural distance.";

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
            this.StatusText = "Recording… speak now.";
            var samples = await recorder.RecordAsync(RecordFor);

            this.StatusText = "Enrolling…";
            await voice.Enroll(name, samples);
            this.shotCount++;
            this.StatusText = $"Enrolled '{name}' — {this.shotCount} sample(s). Record more for accuracy.";
        }
        catch (FileNotFoundException)
        {
            this.StatusText = "Voice model missing — drop ecapa.onnx in Sample/Resources/Raw (see README).";
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
