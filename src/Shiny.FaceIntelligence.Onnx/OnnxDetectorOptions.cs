namespace Shiny.FaceIntelligence.Onnx;

/// <summary>
/// Configures the ONNX face <b>detector</b> (<see cref="OnnxUltraFaceDetector"/>). Provide the model exactly
/// one way (checked in priority order): <see cref="ModelBytesProvider"/> (lazy — preferred for bundled mobile
/// assets), then <see cref="ModelBytes"/>, then <see cref="ModelPath"/>.
/// </summary>
/// <remarks>
/// Defaults target an <b>UltraFace</b> model (version-RFB-320 / slim-320): input <c>1×3×240×320</c> RGB,
/// normalized <c>(px − 127) / 128</c>, two outputs — scores <c>[1, N, 2]</c> and boxes <c>[1, N, 4]</c> with
/// normalized <c>[x1,y1,x2,y2]</c>. If you bundle a different UltraFace export, adjust
/// <see cref="InputWidth"/>/<see cref="InputHeight"/>. A detector with a materially different output layout
/// (SCRFD, RetinaFace, YuNet) needs its own <see cref="IFaceDetector"/> implementation via <c>UseDetector</c>.
/// </remarks>
public class OnnxDetectorOptions
{
    /// <summary>Path to the detector ONNX model. Use for desktop/server or after copying to app data.</summary>
    public string? ModelPath { get; set; }

    /// <summary>Raw model bytes (already in memory).</summary>
    public byte[]? ModelBytes { get; set; }

    /// <summary>
    /// Lazy model-bytes loader, invoked on the <b>first detect</b> (not at startup or DI resolve). Preferred
    /// on iOS/Android: e.g. <c>() => ReadAppPackageBytes("face_detector.onnx")</c>. A missing model therefore
    /// throws <see cref="System.IO.FileNotFoundException"/> from the first enroll/recognize — where the pages
    /// catch it — instead of crashing at launch.
    /// </summary>
    public Func<byte[]>? ModelBytesProvider { get; set; }

    /// <summary>Model input width in pixels. Default 320 (UltraFace RFB-320).</summary>
    public int InputWidth { get; set; } = 320;

    /// <summary>Model input height in pixels. Default 240 (UltraFace RFB-320).</summary>
    public int InputHeight { get; set; } = 240;

    /// <summary>Per-channel mean subtracted during preprocessing. Default 127 (UltraFace).</summary>
    public float Mean { get; set; } = 127f;

    /// <summary>Std the pixels are divided by after mean subtraction. Default 128 (UltraFace).</summary>
    public float Std { get; set; } = 128f;

    /// <summary>
    /// Candidate score floor (0..1): boxes below this are discarded before NMS. This is only the detector's
    /// raw floor — the manager applies the stricter <c>FaceIntelligenceOptions.MinDetectionConfidence</c> on
    /// top. Default 0.5.
    /// </summary>
    public float ScoreThreshold { get; set; } = 0.5f;

    /// <summary>IoU threshold for non-max suppression of overlapping boxes. Default 0.4.</summary>
    public float IouThreshold { get; set; } = 0.4f;
}
