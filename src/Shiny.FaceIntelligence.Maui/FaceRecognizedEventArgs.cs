using Microsoft.Maui.Graphics;

namespace Shiny.FaceIntelligence.Maui;

/// <summary>
/// A recognition attempt completed against a live camera frame. Raised on the UI thread by
/// <see cref="FaceRecognitionAnalyzer"/> every time it commits to running the recognizer — including when
/// nothing matched, so a consumer can show "unknown" rather than silently holding the last name.
/// </summary>
/// <param name="Result">
/// The recognizer's verdict. Check <see cref="RecognitionResult.IsMatch"/>: a no-match still reports here.
/// </param>
/// <param name="Bounds">
/// Where the face was, normalized (0..1) in upright, mirror-corrected image space — the same space
/// <see cref="Shiny.Controls.Camera.OverlayBox"/> uses, so it maps directly onto the preview.
/// </param>
/// <param name="Confidence">The detector's confidence that this region was a face, 0..1.</param>
public record FaceRecognizedEventArgs(RecognitionResult Result, RectF Bounds, float Confidence);
