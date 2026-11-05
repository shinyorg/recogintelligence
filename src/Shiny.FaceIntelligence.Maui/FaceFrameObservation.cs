using Microsoft.Maui.Graphics;

namespace Shiny.FaceIntelligence.Maui;

/// <summary>Usability of a detected crop; absent faces do not imply a lighting diagnosis.</summary>
public enum FaceFrameQuality { Unknown, Usable, TooDark, TooBright, Blurry }

/// <summary>Immutable frame identity assigned before conversion, detection or asynchronous recognition.</summary>
/// <param name="SessionId">Camera generation.</param>
/// <param name="FrameId">Increasing frame identity.</param>
/// <param name="Timestamp">Monotonic timestamp before conversion.</param>
/// <param name="CapturedAtUtc">Device UTC capture time.</param>
public sealed record FaceFrameStamp(Guid SessionId, long FrameId, long Timestamp, DateTime CapturedAtUtc);
/// <summary>Immutable detection, quality and image evidence for one captured frame.</summary>
/// <param name="Stamp">Frame identity and capture timing.</param>
/// <param name="FaceCount">Detected face count.</param>
/// <param name="Bounds">Optional normalized face rectangle.</param>
/// <param name="InsideScanWindow">Whether the native guide check passed.</param>
/// <param name="Confidence">Optional detector confidence from zero to one.</param>
/// <param name="Brightness">Optional normalized crop brightness.</param>
/// <param name="Sharpness">Optional variance-of-Laplacian metric.</param>
/// <param name="Quality">Crop usability classification.</param>
/// <param name="ImageData">Optional encoded upright JPEG.</param>
/// <param name="ImageWidth">JPEG width in pixels.</param>
/// <param name="ImageHeight">JPEG height in pixels.</param>
/// <param name="Error">Optional conversion or detection failure.</param>
public sealed record FaceFrameObservation(FaceFrameStamp Stamp, int FaceCount, RectF? Bounds,
    bool InsideScanWindow, float? Confidence, float? Brightness, float? Sharpness,
    FaceFrameQuality Quality, byte[]? ImageData, int ImageWidth, int ImageHeight, string? Error = null);

/// <summary>Complete recognition, quality, observation and native camera binding contract.</summary>
public interface IFaceRecognitionAnalyzer
{
    /// <summary>Native camera analyzer role required by CameraView; business consumers use this interface.</summary>
    Shiny.Controls.Camera.FrameAnalyzer NativeAnalyzer { get; }
    /// <summary>Whether diagnostic detection boxes are drawn.</summary>
    bool ShowBoundingBox { get; set; }
    /// <summary>Number of stable detections required before comparison.</summary>
    int StabilityFrames { get; set; }
    /// <summary>Normalized motion tolerance between stable detections.</summary>
    float StabilityTolerance { get; set; }
    /// <summary>Minimum time between comparison attempts.</summary>
    TimeSpan RecognitionInterval { get; set; }
    /// <summary>Maximum analyzed width in pixels.</summary>
    int MaxAnalysisWidth { get; set; }
    /// <summary>Minimum detector confidence from zero to one.</summary>
    float MinConfidence { get; set; }
    /// <summary>Minimum usable normalized brightness.</summary>
    float MinBrightness { get; set; }
    /// <summary>Maximum usable normalized brightness.</summary>
    float MaxBrightness { get; set; }
    /// <summary>Minimum variance-of-Laplacian sharpness.</summary>
    float MinSharpness { get; set; }
    /// <summary>Whether comparison runs in addition to continuous detection.</summary>
    bool RecognitionEnabled { get; set; }
    /// <summary>Latest accepted face and original encoded image, or null when absent.</summary>
    AnalyzedFace? LastFace { get; }
    /// <summary>Optional overlay color for matched faces.</summary>
    Color? MatchColor { get; set; }
    /// <summary>Optional overlay color for unmatched faces.</summary>
    Color? UnknownColor { get; set; }
    /// <summary>Diagnostic overlay text for an unmatched face.</summary>
    string UnknownText { get; set; }
    /// <summary>Optional UI command for completed comparisons.</summary>
    System.Windows.Input.ICommand? FaceRecognizedCommand { get; set; }
    /// <summary>Current generation; changes only when a new session begins.</summary>
    Guid SessionId { get; }
    /// <summary>Every detection observation, including missing/multiple faces and quality failures.</summary>
    event EventHandler<FaceFrameObservation>? FrameObserved;
    /// <summary>Accepted single-face detection, independent of comparison cadence.</summary>
    event EventHandler<AnalyzedFace>? FaceDetected;
    /// <summary>A previously accepted face is no longer available.</summary>
    event EventHandler? FaceLost;
    /// <summary>Timestamped comparison outcome for its original frame.</summary>
    event EventHandler<FaceRecognizedEventArgs>? FaceRecognized;
    /// <summary>Starts a fresh observation generation and cancels obsolete comparison work.</summary>
    void BeginSession();
    /// <summary>Closes admission and waits at most the supplied timeout for outstanding comparison work.</summary>
    Task<bool> StopSessionAsync(TimeSpan timeout);
}
