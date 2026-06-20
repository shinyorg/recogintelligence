using CommunityToolkit.Mvvm.ComponentModel;
using Shiny;
using Shiny.FaceIntelligence;
using Sample.Pages;

namespace Sample.ViewModels;

[ShellMap<EnrollPage>("Enroll", registerRoute: false)]
public partial class EnrollViewModel(IFaceIntelligence recognizer) : ObservableObject
{
    int shotCount;

    [ObservableProperty]
    public partial string? Name { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Enter a name, then capture a few shots from different angles.";

    /// <summary>Run the embed + store. Called by the page after it has captured a still + face box.</summary>
    public async Task Process(byte[] photoData, FaceBox box)
    {
        var name = this.Name!.Trim();
        try
        {
            await recognizer.Enroll(name, photoData, box);
            this.shotCount++;
            this.StatusText = $"Enrolled '{name}' — {this.shotCount} shot(s). Capture more for accuracy.";
        }
        catch (FileNotFoundException)
        {
            this.StatusText = "Face model missing — drop arcface.onnx in Sample/Resources/Raw (see README).";
        }
        catch (Exception ex)
        {
            this.StatusText = $"Enroll failed ({ex.GetType().Name}): {ex.Message}";
        }
    }
}
