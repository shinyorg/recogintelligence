using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Shiny.FaceIntelligence.Onnx;

/// <summary>
/// <see cref="IFaceDetector"/> backed by an UltraFace-style ONNX model (version-RFB / slim). Preprocessing:
/// resize the whole frame to the model input, normalize <c>(px − mean) / std</c>, NCHW. The model emits
/// per-anchor <c>scores [1, N, 2]</c> (background, face) and <c>boxes [1, N, 4]</c> (normalized
/// <c>[x1,y1,x2,y2]</c>); we threshold on the face score, run non-max suppression, and map the surviving
/// boxes back to source-image pixels.
/// </summary>
public sealed class OnnxUltraFaceDetector : IFaceDetector, IDisposable
{
    readonly OnnxDetectorOptions options;

    // Lazy session (see OnnxArcFaceEmbedder for the rationale): resolving the detector to nothing must not
    // load the model, so a missing model surfaces from the first Detect (where the pages catch it).
    readonly Lazy<(InferenceSession Session, string InputName)> model;

    public OnnxUltraFaceDetector(Func<InferenceSession> sessionFactory, OnnxDetectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(sessionFactory);
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.model = new Lazy<(InferenceSession, string)>(() =>
        {
            var session = sessionFactory();
            return (session, session.InputMetadata.Keys.First());
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IReadOnlyList<DetectedFace> Detect(ReadOnlySpan<byte> imageData)
    {
        var (session, inputName) = this.model.Value;

        using var src = SKBitmap.Decode(imageData.ToArray())
            ?? throw new InvalidOperationException("Could not decode the captured image.");
        int ow = src.Width, oh = src.Height;

        var input = this.ToTensor(src);
        using var results = session.Run([NamedOnnxValue.CreateFromTensor(inputName, input)]);

        // UltraFace emits two outputs; identify them by their last dimension (scores=2, boxes=4) rather than
        // by name/order, which varies between exports.
        float[]? scores = null, boxes = null;
        foreach (var r in results)
        {
            var t = r.AsTensor<float>();
            var last = t.Dimensions[^1];
            if (last == 2) scores = t.ToArray();
            else if (last == 4) boxes = t.ToArray();
        }
        if (scores is null || boxes is null)
            throw new InvalidOperationException(
                "Detector model outputs weren't the expected UltraFace shape (scores [1,N,2] + boxes [1,N,4]). " +
                "Use a UltraFace export or plug a custom IFaceDetector via UseDetector(...).");

        var n = scores.Length / 2;
        var candidates = new List<DetectedFace>();
        for (var i = 0; i < n; i++)
        {
            var faceScore = scores[(i * 2) + 1]; // [background, face]
            if (faceScore < this.options.ScoreThreshold)
                continue;

            // Normalized [x1,y1,x2,y2] → source pixels, clamped to the image.
            var x1 = Math.Clamp(boxes[i * 4] * ow, 0, ow);
            var y1 = Math.Clamp(boxes[(i * 4) + 1] * oh, 0, oh);
            var x2 = Math.Clamp(boxes[(i * 4) + 2] * ow, 0, ow);
            var y2 = Math.Clamp(boxes[(i * 4) + 3] * oh, 0, oh);
            if (x2 <= x1 || y2 <= y1)
                continue;

            candidates.Add(new DetectedFace(new FaceBox(x1, y1, x2 - x1, y2 - y1), faceScore));
        }

        return NonMaxSuppression(candidates, this.options.IouThreshold);
    }

    DenseTensor<float> ToTensor(SKBitmap src)
    {
        int w = this.options.InputWidth, h = this.options.InputHeight;

        using var dest = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(dest))
        {
            canvas.Clear(SKColors.Black);
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawBitmap(src, new SKRect(0, 0, src.Width, src.Height), new SKRect(0, 0, w, h), paint);
        }

        var tensor = new DenseTensor<float>([1, 3, h, w]);
        var pixels = dest.Pixels; // row-major RGBA
        float mean = this.options.Mean, std = this.options.Std;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var c = pixels[(y * w) + x];
                tensor[0, 0, y, x] = (c.Red - mean) / std;
                tensor[0, 1, y, x] = (c.Green - mean) / std;
                tensor[0, 2, y, x] = (c.Blue - mean) / std;
            }
        }
        return tensor;
    }

    static List<DetectedFace> NonMaxSuppression(List<DetectedFace> boxes, float iouThreshold)
    {
        var ordered = boxes.OrderByDescending(b => b.Confidence).ToList();
        var kept = new List<DetectedFace>();
        while (ordered.Count > 0)
        {
            var best = ordered[0];
            kept.Add(best);
            ordered.RemoveAt(0);
            ordered.RemoveAll(b => IoU(best.Box, b.Box) > iouThreshold);
        }
        return kept;
    }

    static float IoU(FaceBox a, FaceBox b)
    {
        var ix1 = Math.Max(a.X, b.X);
        var iy1 = Math.Max(a.Y, b.Y);
        var ix2 = Math.Min(a.Right, b.Right);
        var iy2 = Math.Min(a.Bottom, b.Bottom);
        var iw = Math.Max(0, ix2 - ix1);
        var ih = Math.Max(0, iy2 - iy1);
        var inter = iw * ih;
        var union = (a.Width * a.Height) + (b.Width * b.Height) - inter;
        return union <= 0 ? 0 : inter / union;
    }

    public void Dispose()
    {
        if (this.model.IsValueCreated)
            this.model.Value.Session.Dispose();
    }
}
