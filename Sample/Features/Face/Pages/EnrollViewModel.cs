using CommunityToolkit.Mvvm.ComponentModel;
using Shiny;
using Shiny.FaceIntelligence.Maui;

namespace Sample.Features.Face.Pages;

[ShellMap<EnrollPage>("Enroll", registerRoute: false)]
public partial class EnrollViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string? Name { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } =
        "Enter a name, then start — you'll be prompted through a few angles and distances.";

    /// <summary>Kick off the guided sequence. The control handles every step from here.</summary>
    public void StartEnrollment(FaceEnrollmentView camera)
    {
        try
        {
            camera.BeginEnrollment();
            this.StatusText = "Follow the prompts on the preview.";
        }
        catch (Exception ex)
        {
            this.StatusText = ex.Message;
        }
    }

    /// <summary>
    /// Report the finished gallery. MinPairwiseDistance is the useful number: it says how much spread the
    /// captured shots actually have, which is what the prompts were for.
    /// </summary>
    public void Show(FaceEnrollmentResult result)
    {
        var skipped = result.SkippedSteps > 0 ? $", {result.SkippedSteps} step(s) skipped" : String.Empty;
        this.StatusText =
            $"Enrolled {result.People.Count} shots for '{result.Name}' — closest pair {result.MinPairwiseDistance:F3}{skipped}.";
    }
}
