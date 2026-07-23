using Sample.Features.Voice;
using Shiny.VoiceIntelligence;

namespace Sample.Features.Voice.Pages;

// The control owns the sentences, the countdown, recording and every quality decision; the page just
// starts it and reports the outcome. Mirrors EnrollPage on the face side.
public partial class VoiceEnrollPage : ContentPage
{
    public VoiceEnrollPage()
    {
        this.InitializeComponent();

        // Enroll and identify record for the SAME duration on purpose — see VoiceTuning.RecordFor. Set here
        // rather than left on the control's default so the two can't drift apart.
        this.VoiceEnroller.RecordFor = VoiceTuning.RecordFor;
    }

    void OnStartClicked(object? sender, EventArgs e)
    {
        if (this.BindingContext is not VoiceEnrollViewModel vm)
            return;

        if (String.IsNullOrWhiteSpace(vm.Name))
        {
            vm.StatusText = "Enter a name first.";
            return;
        }

        vm.StartEnrollment(this.VoiceEnroller);
    }

    void OnCancelClicked(object? sender, EventArgs e)
    {
        this.VoiceEnroller.CancelEnrollment();
        (this.BindingContext as VoiceEnrollViewModel)?.Cancelled();
    }

    void OnCompleted(object? sender, VoiceEnrollmentResult e)
        => (this.BindingContext as VoiceEnrollViewModel)?.Show(e);

    void OnFailed(object? sender, string reason)
        => (this.BindingContext as VoiceEnrollViewModel)?.Stopped(reason);

    // Leaving the tab mid-run would otherwise leave the mic open behind a page nobody is looking at.
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        this.VoiceEnroller.CancelEnrollment();
        (this.BindingContext as VoiceEnrollViewModel)?.Cancelled();
    }
}
