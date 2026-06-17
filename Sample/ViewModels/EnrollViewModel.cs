using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shiny;
using Shiny.FaceIntelligence;
using Sample.Pages;

namespace Sample.ViewModels;

[ShellMap<EnrollPage>("Enroll", registerRoute: false)]
public partial class EnrollViewModel(IFaceIntelligence recognizer) : ObservableObject
{
    bool armed;
    int shotCount;

    [ObservableProperty]
    public partial string? Name { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Enter a name, then capture a few shots from different angles.";

    [RelayCommand]
    void Enroll()
    {
        if (string.IsNullOrWhiteSpace(this.Name))
        {
            this.StatusText = "Enter a name first.";
            return;
        }
        // Arm: the next detected+captured face gets enrolled under this name.
        this.armed = true;
        this.StatusText = "Hold still — looking for a face…";
    }

    /// <summary>
    /// Called by the page's <c>DetectionCaptured</c> handler. Consumes one capture per arm.
    /// </summary>
    public async Task Process(byte[] photoData, FaceBox box)
    {
        if (!this.armed)
            return;

        this.armed = false; // consume this capture
        var name = this.Name!.Trim();
        try
        {
            await recognizer.Enroll(name, photoData, box);
            this.shotCount++;
            this.StatusText = $"Enrolled '{name}' — {this.shotCount} shot(s). Capture more for accuracy.";
        }
        catch (FileNotFoundException)
        {
            this.StatusText = "Face model missing — add arcface.onnx (see README).";
        }
        catch (Exception ex)
        {
            this.StatusText = $"Error: {ex.Message}";
        }
    }
}
