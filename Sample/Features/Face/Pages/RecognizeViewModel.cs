using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Shiny;
using Shiny.FaceIntelligence.Maui;

namespace Sample.Features.Face.Pages;

[ShellMap<RecognizePage>("Recognize", registerRoute: false)]
public partial class RecognizeViewModel(ILogger<RecognizeViewModel> logger) : ObservableObject
{
    [ObservableProperty]
    public partial string ResultText { get; set; } = "Point the camera at a face…";

    /// <summary>Second line of on-screen text: pipeline state, so a stalled stage is visible.</summary>
    [ObservableProperty]
    public partial string DiagnosticText { get; set; } = "waiting for camera…";

    /// <summary>Progress note from the page — shown under the result and logged for `maui devflow logs`.</summary>
    public void Note(string message)
    {
        this.DiagnosticText = message;
        logger.LogInformation("[Recognize] {Message}", message);
        Console.WriteLine($"[Recognize] {message}"); // console.out is what `maui devflow logs` surfaces
    }

    /// <summary>
    /// Render a recognition the analyzer produced. All the work — detect, embed, match, throttle — already
    /// happened on the analysis thread; this is display only, on the UI thread.
    /// </summary>
    public void Show(FaceRecognizedEventArgs e)
    {
        this.ResultText = e.Result.IsMatch
            ? $"{e.Result.PersonIdentifier}  ·  {e.Result.Similarity:P0}"
            : "Unknown face";

        this.Note(e.Result.IsMatch
            ? $"matched distance={e.Result.Distance:F3} confidence={e.Confidence:P0}"
            : $"no match within threshold (confidence={e.Confidence:P0})");
    }
}
