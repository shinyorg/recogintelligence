using System.Windows.Input;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using SkiaSharp;

namespace Shiny.FaceIntelligence.Maui;

/// <summary>
/// A <c>CameraView</c> frame analyzer that runs Shiny.FaceIntelligence recognition on the live
/// preview — no still capture, no second detector. Every frame goes through the registered
/// <see cref="IFaceDetector"/> (cheap) to draw a live box; the full recognize pipeline (ArcFace embed +
/// vector search) only runs once the face has held steady for <see cref="StabilityFrames"/> frames, and at
/// most once per <see cref="RecognitionInterval"/> after that.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the older "subscribe to a camera face analyzer, then call <c>CapturePhotoAsync</c> per
/// detection" arrangement. That path had to reconcile two coordinate spaces (the camera's normalized bounds
/// vs. the library's pixel <see cref="FaceBox"/>), paid a full still capture per detection, and depended on
/// capture working from a frame callback. Here the frame is converted once into upright, mirror-corrected
/// image space and everything — detection, embedding, and the overlay box — is expressed in it.
/// </para>
/// <para>
/// Recognition is continuous and ungated: unlike a barcode scan there is nothing to "arm", so results are
/// raised as they happen rather than through the base class's arm/deliver handshake.
/// </para>
/// </remarks>
public class FaceRecognitionAnalyzer : FrameAnalyzer
{
    readonly IFaceIntelligence intelligence;
    readonly IFaceDetector detector;
    readonly FrameImageConverter converter = new();

    int stableFrames;
    RectF? lastBounds;
    DateTimeOffset lastRecognizedAt = DateTimeOffset.MinValue;
    RecognitionResult? lastResult;

    /// <param name="intelligence">The recognizer — the same <see cref="IFaceIntelligence"/> the enroll path uses.</param>
    /// <param name="detector">
    /// The per-frame face detector. Register one with <c>UseOnnxDetector(...)</c> (or <c>UseDetector(...)</c>);
    /// this analyzer needs it directly because it wants a box on every frame, not just when embedding.
    /// </param>
    public FaceRecognitionAnalyzer(IFaceIntelligence intelligence, IFaceDetector detector)
    {
        this.intelligence = intelligence ?? throw new ArgumentNullException(nameof(intelligence));
        this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
    }

    /// <inheritdoc/>
    public override string Id => "shiny.faceintelligence.recognition";

    /// <summary>
    /// How many consecutive frames a face must stay put before the recognizer runs. Debounces a moving or
    /// half-in-frame face so an embed isn't spent on a shot that would miss anyway. Default 3.
    /// </summary>
    public int StabilityFrames { get; set; } = 3;

    /// <summary>
    /// How far a face's center may move between frames (as a fraction of the frame) and still count as
    /// "held steady". Default 0.05 — 5% of the frame.
    /// </summary>
    public float StabilityTolerance { get; set; } = 0.05f;

    /// <summary>
    /// Minimum time between two recognize runs once a face is stable. Without this, a person sitting still
    /// would re-embed and re-query at frame rate for the same answer. Default 2 seconds.
    /// </summary>
    public TimeSpan RecognitionInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Frames are downscaled so they're no wider than this before detection/embedding. Bounds the per-frame
    /// cost; the detector runs at 320×240 anyway. Default 720.
    /// </summary>
    public int MaxAnalysisWidth { get; set; } = 720;

    /// <summary>Detections below this confidence are ignored entirely (no box, no recognition). Default 0.7.</summary>
    public float MinConfidence { get; set; } = 0.7f;

    /// <summary>
    /// Whether the recognize stage runs at all. Default <c>true</c>. Set <c>false</c> for an enroll screen:
    /// detection keeps running (so the box still draws and <see cref="LastFace"/> stays fresh) but no
    /// embedding or vector query is spent on frames nobody is matching.
    /// </summary>
    public bool RecognitionEnabled { get; set; } = true;

    /// <summary>
    /// The most recent frame that contained an accepted face, as the exact encoded bytes and pixel
    /// <see cref="FaceBox"/> the recognizer was (or would be) handed. <c>null</c> until a face is seen, and
    /// cleared when the face leaves the frame.
    /// </summary>
    /// <remarks>
    /// This is what makes enrolling from the live preview correct rather than merely convenient: enrolling
    /// these bytes with this box means the stored template went through <b>identical</b> preprocessing
    /// (same orientation, same mirror correction, same downscale, same crop) as every probe it will later be
    /// compared against. Enrolling from a separately captured still does not, and a systematic preprocessing
    /// difference between template and probe shifts every distance in the gallery.
    /// </remarks>
    public AnalyzedFace? LastFace { get; private set; }

    /// <summary>Box/caption color for a face that matched someone. Null uses the overlay default.</summary>
    public Color? MatchColor { get; set; } = Colors.LimeGreen;

    /// <summary>Box/caption color for a face that didn't match. Null uses the overlay default.</summary>
    public Color? UnknownColor { get; set; } = Colors.OrangeRed;

    /// <summary>Caption drawn on the box before the first recognition completes, and for a no-match face.</summary>
    public string UnknownText { get; set; } = "Unknown";

    /// <summary>Raised on the UI thread after each recognition attempt — match or not.</summary>
    public event EventHandler<FaceRecognizedEventArgs>? FaceRecognized;

    /// <summary>
    /// Raised on the UI thread for <b>every</b> frame containing an accepted face, independent of
    /// recognition (so it still fires with <see cref="RecognitionEnabled"/> off). Guided enrollment drives
    /// off this: it needs to evaluate each frame's geometry and quality, not just completed matches.
    /// </summary>
    public event EventHandler<AnalyzedFace>? FaceDetected;

    /// <summary>Raised on the UI thread when a previously-tracked face leaves the frame.</summary>
    public event EventHandler? FaceLost;

    /// <summary>Command form of <see cref="FaceRecognized"/>, for XAML binding.</summary>
    public static readonly BindableProperty FaceRecognizedCommandProperty = BindableProperty.Create(
        nameof(FaceRecognizedCommand), typeof(ICommand), typeof(FaceRecognitionAnalyzer));

    /// <inheritdoc cref="FaceRecognizedCommandProperty"/>
    public ICommand? FaceRecognizedCommand
    {
        get => (ICommand?)this.GetValue(FaceRecognizedCommandProperty);
        set => this.SetValue(FaceRecognizedCommandProperty, value);
    }

    /// <inheritdoc/>
    public override async ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeAsync(CameraFrame frame, CancellationToken ct)
    {
        if (!this.IsEnabled)
            return null;

        using var bmp = this.converter.ToUpright(frame, this.MaxAnalysisWidth);
        if (bmp is null || bmp.Width == 0 || bmp.Height == 0)
            return null;

        // Both the detector and the recognizer take encoded bytes, so encode once and reuse for both stages.
        using var encoded = bmp.Encode(SKEncodedImageFormat.Jpeg, 85);
        if (encoded is null)
            return null;
        var bytes = encoded.ToArray();

        var best = this.BestFace(bytes);
        if (best is null)
        {
            // Face gone: drop the boxes and reset, so walking away then back re-recognizes immediately.
            var had = this.LastFace is not null;
            this.stableFrames = 0;
            this.lastBounds = null;
            this.lastResult = null;
            this.LastFace = null;
            if (had)
                this.Raise(() => this.FaceLost?.Invoke(this, EventArgs.Empty));
            return null;
        }

        var face = best.Value;
        var bounds = Normalize(face.Box, bmp.Width, bmp.Height);
        if (!this.InScanWindow(bounds))
        {
            this.stableFrames = 0;
            this.lastBounds = null;
            return null;
        }

        this.stableFrames = this.IsSteady(bounds) ? this.stableFrames + 1 : 1;
        this.lastBounds = bounds;
        var analyzed = new AnalyzedFace(bytes, face.Box, bounds, face.Confidence, this.stableFrames, bmp.Width, bmp.Height);
        this.LastFace = analyzed;
        this.Raise(() => this.FaceDetected?.Invoke(this, analyzed));

        if (this.RecognitionEnabled && this.ShouldRecognize())
        {
            this.lastRecognizedAt = DateTimeOffset.UtcNow;
            try
            {
                var result = await this.intelligence.Recognize(bytes, face.Box, ct).ConfigureAwait(false);
                this.lastResult = result;

                var args = new FaceRecognizedEventArgs(result, bounds, face.Confidence);
                this.Raise(() =>
                {
                    this.FaceRecognized?.Invoke(this, args);
                    if (this.FaceRecognizedCommand?.CanExecute(args) == true)
                        this.FaceRecognizedCommand.Execute(args);
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // A failed recognize (missing model, store error) must not kill the analysis loop — keep
                // drawing boxes and let the next interval retry. The consumer sees no event.
                this.lastResult = null;
            }
        }

        return this.ShowBoundingBox ? [this.ToOverlay(bounds)] : null;
    }

    DetectedFace? BestFace(byte[] imageData)
    {
        DetectedFace? best = null;
        foreach (var f in this.detector.Detect(imageData))
        {
            if (f.Confidence < this.MinConfidence)
                continue;
            if (best is null || f.Confidence > best.Value.Confidence)
                best = f;
        }
        return best;
    }

    bool IsSteady(RectF bounds)
    {
        if (this.lastBounds is not { } prev)
            return false;

        var dx = (bounds.X + bounds.Width / 2f) - (prev.X + prev.Width / 2f);
        var dy = (bounds.Y + bounds.Height / 2f) - (prev.Y + prev.Height / 2f);
        return MathF.Sqrt(dx * dx + dy * dy) <= this.StabilityTolerance;
    }

    bool ShouldRecognize()
        => this.stableFrames >= this.StabilityFrames &&
           DateTimeOffset.UtcNow - this.lastRecognizedAt >= this.RecognitionInterval;

    OverlayBox ToOverlay(RectF bounds)
    {
        var matched = this.lastResult?.IsMatch == true;
        var color = matched ? this.MatchColor : this.UnknownColor;
        var text = matched
            ? $"{this.lastResult!.Name} · {this.lastResult.Similarity:P0}"
            : this.UnknownText;

        return new OverlayBox(bounds, color, text, color);
    }

    static RectF Normalize(FaceBox box, int width, int height)
        => new(box.X / width, box.Y / height, box.Width / width, box.Height / height);
}
