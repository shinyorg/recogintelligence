using Microsoft.Maui.Graphics;

namespace Shiny.FaceIntelligence.Maui;

/// <summary>
/// Usability of the detected face crop. <see cref="Unknown"/> when there was no face, or when quality wasn't
/// measured (it is only measured while <see cref="FaceRecognitionAnalyzer.FrameObserved"/> has a subscriber).
/// </summary>
public enum FaceFrameQuality { Unknown, Usable, TooDark, TooBright, Blurry }

/// <summary>Identity of one analyzed frame, assigned before conversion so async results can be traced back to it.</summary>
/// <param name="SessionId">The <see cref="FaceRecognitionAnalyzer.SessionId"/> the frame belongs to.</param>
/// <param name="FrameId">Increases by one per analyzed frame (dropped frames are never analyzed, so gaps are normal).</param>
/// <param name="Timestamp"><see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> when analysis began — monotonic, for ordering and latency.</param>
/// <param name="CapturedAtUtc">
/// UTC wall-clock time when analysis of the frame <b>began</b>. The camera frame carries no capture timestamp,
/// so this trails the real exposure by the pipeline latency.
/// </param>
public sealed record FaceFrameStamp(Guid SessionId, long FrameId, long Timestamp, DateTime CapturedAtUtc);
/// <summary>Immutable detection, quality and image evidence for one captured frame.</summary>
/// <param name="Stamp">Frame identity and capture timing.</param>
/// <param name="FaceCount">Faces detected above <c>MinConfidence</c>; when more than one, the rest of the record describes the most confident.</param>
/// <param name="Bounds">Normalized (upright image space) rectangle of the tracked face, or null when there was none.</param>
/// <param name="InsideScanWindow">Whether the native guide check passed.</param>
/// <param name="Confidence">Optional detector confidence from zero to one.</param>
/// <param name="Brightness">Mean crop brightness 0..1, when measured.</param>
/// <param name="Sharpness">Variance-of-Laplacian of the crop, when measured.</param>
/// <param name="Quality">Crop usability classification.</param>
/// <param name="ImageData">Optional encoded upright JPEG.</param>
/// <param name="ImageWidth">JPEG width in pixels.</param>
/// <param name="ImageHeight">JPEG height in pixels.</param>
/// <param name="Error">Optional conversion or detection failure.</param>
public sealed record FaceFrameObservation(FaceFrameStamp Stamp, int FaceCount, RectF? Bounds,
    bool InsideScanWindow, float? Confidence, float? Brightness, float? Sharpness,
    FaceFrameQuality Quality, byte[]? ImageData, int ImageWidth, int ImageHeight, string? Error = null);
