using CommunityToolkit.Mvvm.ComponentModel;
using Shiny;
using Shiny.FaceIntelligence;

namespace Sample.Features.Face.Pages;

[ShellMap<EnrollPage>("Enroll", registerRoute: false)]
public partial class EnrollViewModel(IFaceIntelligence recognizer, IDialogs dialogs) : ObservableObject
{
    int shotCount;

    [ObservableProperty]
    public partial string? Name { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Enter a name, then capture a few shots from different angles.";

    /// <summary>
    /// Run detect + embed + store on a captured still. The ONNX detector finds the face and the manager
    /// enforces the quality gates, so we translate its typed errors into a status message instead of
    /// silently enrolling a bad shot.
    /// </summary>
    public async Task Process(byte[] photoData)
    {
        var name = this.Name!.Trim();
        try
        {
            await recognizer.Enroll(name, photoData);
            this.shotCount++;
            this.StatusText = $"Enrolled '{name}' — {this.shotCount} shot(s). Capture more for accuracy.";
        }
        catch (FaceDetectionException ex)
        {
            // No/low-confidence/multiple/too-small face — ex.Message already says what to fix.
            this.StatusText = ex.Message;
        }
        catch (FaceEnrollmentConflictException ex)
        {
            // This face already matches someone else — confirm before forcing a new identity.
            var enrollAnyway = await dialogs.Confirm(
                "Looks familiar",
                $"This looks like '{ex.Match.Name}'. Enroll as '{name}' anyway?",
                "Enroll anyway",
                "Cancel");

            if (!enrollAnyway)
            {
                this.StatusText = $"Cancelled — this face matched '{ex.Match.Name}'.";
                return;
            }

            await recognizer.Enroll(name, photoData, allowDuplicate: true);
            this.shotCount++;
            this.StatusText = $"Enrolled '{name}' — {this.shotCount} shot(s). Capture more for accuracy.";
        }
        catch (FileNotFoundException)
        {
            this.StatusText = "Model missing — drop arcface.onnx and face_detector.onnx in Sample/Resources/Raw (see README).";
        }
        catch (Exception ex)
        {
            this.StatusText = $"Enroll failed ({ex.GetType().Name}): {ex.Message}";
        }
    }
}
