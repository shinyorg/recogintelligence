using CommunityToolkit.Mvvm.ComponentModel;
using Shiny;
using Shiny.FaceIntelligence;
using Sample.Pages;

namespace Sample.ViewModels;

[ShellMap<RecognizePage>("Recognize", registerRoute: false)]
public partial class RecognizeViewModel(IFaceIntelligence recognizer) : ObservableObject
{
    bool busy;
    bool disabled;

    [ObservableProperty]
    public partial string ResultText { get; set; } = "Point the camera at a face…";

    /// <summary>
    /// Called by the page's <c>DetectionCaptured</c> handler with the captured still and the
    /// largest face's box. Recognition logic lives here; the page only owns the camera hardware.
    /// </summary>
    public async Task Process(byte[] photoData, FaceBox box)
    {
        // Don't pile up work, and stop trying once we know the model is missing.
        if (this.disabled || this.busy)
            return;

        this.busy = true;
        try
        {
            var result = await recognizer.Recognize(photoData, box);
            this.ResultText = result.IsMatch
                ? $"{result.Name}  ·  {result.Similarity:P0}"
                : "Unknown face";
        }
        catch (FileNotFoundException)
        {
            this.disabled = true;
            this.ResultText = "Face model missing — add arcface.onnx (see README).";
        }
        catch (Exception ex)
        {
            this.ResultText = $"Error: {ex.Message}";
        }
        finally
        {
            this.busy = false;
        }
    }
}
