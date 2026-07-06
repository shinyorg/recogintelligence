using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sample.Features.Voice.Audio;
using Shiny;
using Shiny.VoiceIntelligence;

namespace Sample.Features.Voice.Pages;

// Records a short utterance on tap and reports the nearest enrolled speaker. Unlike face Recognize
// (continuous off the camera), audio recognition is button-driven — you can't passively sample a voice.
[ShellMap<VoiceRecognizePage>("VoiceRecognize", registerRoute: false)]
public partial class VoiceRecognizeViewModel(IVoiceIntelligence voice, VoiceRecorder recorder) : ObservableObject
{
    static readonly TimeSpan RecordFor = TimeSpan.FromSeconds(4);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    public bool IsIdle => !this.IsBusy;

    [ObservableProperty]
    public partial string ResultText { get; set; } = "Tap and speak to identify the speaker.";

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
                ? $"{result.Name}  ·  {result.Similarity:P0}"
                : "Unknown speaker";
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
}
