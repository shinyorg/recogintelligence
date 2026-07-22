using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sample.Features.Voice.Audio;
using Shiny;
using Shiny.VoiceIntelligence;

namespace Sample.Features.Voice.Pages;

/// <summary>
/// The guided enrollment wizard: show a sentence, record it, keep going until the library says the
/// voiceprints agree well enough to stop. All of the deciding — is this clip usable, does it match the
/// earlier ones, is that enough — lives in <see cref="VoiceEnrollmentSession"/>; this page records and
/// renders.
/// </summary>
/// <remarks>
/// The mic is the audio twin of the face Enroll camera, and this is the twin of <c>FaceEnrollmentView</c>'s
/// step sequence — except the wizard has no fixed number of steps. Face wants varied shots and can count
/// them off; voice wants clips that agree, which no fixed count can guarantee, so the session decides when
/// to stop. A bad recording extends the run instead of poisoning the gallery.
/// </remarks>
[ShellMap<VoiceEnrollPage>("VoiceEnroll", registerRoute: false)]
public partial class VoiceEnrollViewModel(IVoiceIntelligence voice, VoiceRecorder recorder) : ObservableObject
{
    // Enroll and identify record for the SAME duration on purpose — see VoiceTuning.RecordFor.
    static readonly TimeSpan RecordFor = VoiceTuning.RecordFor;

    VoiceEnrollmentSession? session;

    [ObservableProperty]
    public partial string? Name { get; set; }

    [ObservableProperty]
    public partial string PromptText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    public bool IsIdle => !this.IsBusy;

    /// <summary>True between Start and completion — the page swaps the Start button for Record while set.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEnrolling))]
    public partial bool IsEnrolling { get; set; }

    /// <summary>The name entry and Start button show when a run isn't in progress (no inverse-bool converter needed).</summary>
    public bool IsNotEnrolling => !this.IsEnrolling;

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Enter a name and tap Start. You'll be given sentences to read.";

    /// <summary>Why the last recording was turned down. Empty when it was accepted.</summary>
    [ObservableProperty]
    public partial string HintText { get; set; } = "";

    [ObservableProperty]
    public partial string ProgressText { get; set; } = "";

    /// <summary>Accepted recordings over the minimum wanted — drives the progress bar.</summary>
    [ObservableProperty]
    public partial double Progress { get; set; }

    [RelayCommand]
    void Start()
    {
        var name = this.Name?.Trim();
        if (String.IsNullOrWhiteSpace(name))
        {
            this.StatusText = "Enter a name first.";
            return;
        }

        // Options (prompt list, how many clips, how closely they must agree) come from the recognizer's own
        // MaxDistance — see VoiceEnrollmentOptions.ForThreshold.
        this.session = voice.CreateEnrollment(name);
        this.IsEnrolling = true;
        this.HintText = "";
        this.Progress = 0;
        this.PromptText = this.session.CurrentPrompt;
        this.StatusText = $"Tap Record and read the sentence aloud for {RecordFor.TotalSeconds:N0} seconds.";
        this.ProgressText = $"0 of {this.session.Options.MinSamples} good recordings";
    }

    [RelayCommand]
    async Task Record()
    {
        if (this.session is null || this.IsBusy)
            return;

        this.IsBusy = true;
        try
        {
            this.HintText = "";
            this.StatusText = "Recording… read the sentence aloud now.";
            var samples = await recorder.RecordAsync(RecordFor);

            this.StatusText = "Checking…";
            var step = await this.session.Submit(samples);

            this.PromptText = this.session.CurrentPrompt;
            this.HintText = step.Hint;
            this.Show(step);
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

    /// <summary>Abandon the run. Nothing was stored yet, so there is nothing to clean up.</summary>
    [RelayCommand]
    void Cancel()
    {
        this.session = null;
        this.IsEnrolling = false;
        this.PromptText = "";
        this.ProgressText = "";
        this.HintText = "";
        this.Progress = 0;
        this.StatusText = "Cancelled — nothing was saved.";
    }

    void Show(VoiceEnrollmentStepResult step)
    {
        var s = this.session!;
        var min = s.Options.MinSamples;
        this.Progress = Math.Min(1d, s.AcceptedCount / (double)min);

        // Agreement (worst pair) is the number worth showing: it's how much of the matching budget this
        // person's own recordings already spend before a probe is even compared against them.
        var agreement = s.AcceptedCount > 1 ? $" · agreement {s.Cohesion:F2}" : "";
        this.ProgressText = $"{s.AcceptedCount} of {min} good recordings{agreement} · {s.AttemptCount} attempt(s)";

        if (step.Result is { } result)
        {
            this.IsEnrolling = false;
            this.session = null;
            this.PromptText = "";
            this.StatusText = result.IsConfident
                ? $"Done — '{result.Name}' enrolled from {result.Speakers.Count} recordings (agreement {result.Cohesion:F2})."
                : $"Enrolled '{result.Name}' from {result.Speakers.Count} recordings, but they varied more than ideal " +
                  $"(agreement {result.Cohesion:F2}). Re-enroll somewhere quieter if recognition is unreliable.";
            return;
        }

        this.StatusText = step.Accepted
            ? $"Good. {s.SamplesStillNeeded} more to go — read the next sentence."
            : "Let's try that one again.";
    }
}
