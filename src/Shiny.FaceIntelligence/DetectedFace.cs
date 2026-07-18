namespace Shiny.FaceIntelligence;

/// <summary>
/// A face located by an <see cref="IFaceDetector"/>: its <see cref="FaceBox"/> in pixel coordinates plus
/// the detector's <see cref="Confidence"/> (0..1, higher = surer it's a face). The manager uses the
/// confidence and box size to accept or reject a shot at enrollment.
/// </summary>
/// <param name="Box">The face region in source-image pixels.</param>
/// <param name="Confidence">Detector confidence that this region is a face, in 0..1.</param>
public readonly record struct DetectedFace(FaceBox Box, float Confidence);
