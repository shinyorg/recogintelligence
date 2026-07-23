using CommunityToolkit.Mvvm.ComponentModel;
using Shiny;
using Shiny.VoiceIntelligence;
using Shiny.VoiceIntelligence.Maui;

namespace Sample.Features.Voice.Pages;

/// <summary>
/// Drives <see cref="VoiceEnrollmentView"/> and reports the outcome. The sentence list, countdown,
/// recording, quality gating and the decision about when there are enough agreeing recordings all live in
/// the control — this holds a name and a status line.
/// </summary>
/// <remarks>
/// The twin of the face <c>EnrollViewModel</c>, except the run has no fixed number of steps. Face wants
/// varied shots and can count them off; voice wants clips that <i>agree</i>, which no fixed count can
/// guarantee, so the session decides when to stop and a bad recording extends the run instead of poisoning
/// the set.
/// </remarks>
[ShellMap<VoiceEnrollPage>("VoiceEnroll", registerRoute: false)]
public partial class VoiceEnrollViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string? Name { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Enter a name and tap Start. You'll be given sentences to read.";

    /// <summary>True between Start and completion.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEnrolling))]
    public partial bool IsEnrolling { get; set; }

    /// <summary>The name entry and Start button show when a run isn't in progress (no inverse-bool converter needed).</summary>
    public bool IsNotEnrolling => !this.IsEnrolling;

    /// <summary>Kick off the guided run. The control handles every recording from here.</summary>
    public void StartEnrollment(VoiceEnrollmentView enroller)
    {
        try
        {
            enroller.BeginEnrollment();
            this.IsEnrolling = true;
            this.StatusText = "Read each sentence aloud while it's recording.";
        }
        catch (Exception ex)
        {
            this.StatusText = ex.Message;
        }
    }

    /// <summary>
    /// Report the finished enrollment. Agreement (cohesion) is the number worth showing: the worst distance
    /// between any two stored recordings, i.e. how much of the matching budget this person's own templates
    /// spend before a probe is even compared against them.
    /// </summary>
    public void Show(VoiceEnrollmentResult result)
    {
        this.IsEnrolling = false;
        this.StatusText = result.IsConfident
            ? $"Done — '{result.PersonIdentifier}' enrolled from {result.Speakers.Count} recordings (agreement {result.Cohesion:F2})."
            : $"Enrolled '{result.PersonIdentifier}' from {result.Speakers.Count} recordings, but they varied more than ideal " +
              $"(agreement {result.Cohesion:F2}). Re-enroll somewhere quieter if recognition is unreliable.";
    }

    /// <summary>The run stopped without storing anything — the control's message already explains why.</summary>
    public void Stopped(string reason)
    {
        this.IsEnrolling = false;
        this.StatusText = reason;
    }

    /// <summary>Abandoned. Nothing was stored — the session writes only on completion.</summary>
    public void Cancelled()
    {
        if (!this.IsEnrolling)
            return;

        this.IsEnrolling = false;
        this.StatusText = "Cancelled — nothing was saved.";
    }
}
