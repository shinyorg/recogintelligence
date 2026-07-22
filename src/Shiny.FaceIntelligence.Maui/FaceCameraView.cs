namespace Shiny.FaceIntelligence.Maui;

/// <summary>
/// Former name of <see cref="FaceRecognitionView"/>, kept so existing XAML and code keep compiling.
/// </summary>
/// <remarks>
/// Renamed when guided enrollment moved into its own control: "FaceCameraView" said nothing about which of
/// the two jobs it did. Use <see cref="FaceRecognitionView"/> for continuous identification and
/// <see cref="FaceEnrollmentView"/> for capturing a gallery.
/// </remarks>
[Obsolete("Renamed to FaceRecognitionView; use FaceEnrollmentView for guided enrollment.")]
public class FaceCameraView : FaceRecognitionView;
