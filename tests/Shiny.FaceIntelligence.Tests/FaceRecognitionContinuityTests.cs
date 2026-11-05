global using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.FaceIntelligence.Maui;
using Shiny.Controls.Camera;
using SkiaSharp;
using Xunit;

namespace Shiny.FaceIntelligence.Tests
{
    public class FaceRecognitionContinuityTests
    {
        sealed class Detector : IFaceDetector
        {
            public int Count { get; set; } = 1;
            public IReadOnlyList<DetectedFace> Detect(ReadOnlySpan<byte> imageData)
                => Enumerable.Range(0, Count).Select(_ => new DetectedFace(new FaceBox(2, 2, 20, 20), 0.9f)).ToArray();
        }
        sealed class Intelligence : IFaceIntelligence
        {
            public TaskCompletionSource<RecognitionResult> Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task<RecognitionResult> Recognize(byte[] data, FaceBox box, CancellationToken ct = default) { Entered.TrySetResult(); return Pending.Task; }
            public Task<RecognitionResult> Recognize(byte[] data, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<Person> Enroll(string id, byte[] data, FaceBox box, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<Person> Enroll(string id, byte[] data, bool duplicate = false, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<IReadOnlyList<Person>> GetAll(CancellationToken ct = default) => throw new NotSupportedException();
            public Task<int> Forget(string id, CancellationToken ct = default) => throw new NotSupportedException();
        }

        [Fact]
        public async Task SlowRecognition_DoesNotBlockDetection_AndStoppedSessionCannotDeliverOldResult()
        {
            var intelligence = new Intelligence(); var detector = new Detector();
            var analyzer = new FaceRecognitionAnalyzer(intelligence, detector) { StabilityFrames = 1, MinSharpness = 0 };
            var observations = new List<FaceFrameObservation>(); var recognized = new List<FaceRecognizedEventArgs>();
            analyzer.FrameObserved += (_, e) => observations.Add(e);
            analyzer.FaceRecognized += (_, e) => recognized.Add(e);
            using var bitmap = new SKBitmap(32, 32); bitmap.Erase(SKColors.Gray);
            var frame = new CameraFrame { Bitmap = bitmap };
            await analyzer.AnalyzeAsync(frame, CancellationToken.None);
            await intelligence.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            detector.Count = 0;
            await analyzer.AnalyzeAsync(frame, CancellationToken.None);
            Assert.Equal(2, observations.Count); Assert.Equal(0, observations[1].FaceCount); Assert.Null(analyzer.LastFace);
            var oldSession = analyzer.SessionId;
            var stopping = analyzer.StopSessionAsync(TimeSpan.FromSeconds(5));
            intelligence.Pending.SetResult(new("expected", 0.2f, "shot"));
            Assert.True(await stopping); Assert.Empty(recognized);
            analyzer.BeginSession(); Assert.NotEqual(oldSession, analyzer.SessionId);
            await analyzer.AnalyzeAsync(frame, CancellationToken.None);
            Assert.Equal(analyzer.SessionId, observations[^1].Stamp.SessionId);
        }

        [Fact]
        public async Task MultipleFaces_AndConversionFailures_AreExplicitObservations()
        {
            var analyzer = new FaceRecognitionAnalyzer(new Intelligence(), new Detector { Count = 2 });
            var observed = new List<FaceFrameObservation>(); analyzer.FrameObserved += (_, e) => observed.Add(e);
            using var bitmap = new SKBitmap(32, 32);
            await analyzer.AnalyzeAsync(new CameraFrame { Bitmap = bitmap }, CancellationToken.None);
            await analyzer.AnalyzeAsync(new CameraFrame(), CancellationToken.None);
            Assert.Equal(2, observed[0].FaceCount); Assert.NotNull(observed[1].Error); Assert.Null(analyzer.LastFace);
            Assert.True(observed[1].Stamp.FrameId > observed[0].Stamp.FrameId);
        }
    }
}

// Only camera/UI dispatch is replaced. The linked production analyzer, quality and conversion code run unchanged.
namespace Microsoft.Maui.Controls
{
    public sealed class BindableProperty
    {
        public static BindableProperty Create(string name, Type type, Type owner) => new();
    }
}
namespace Shiny.Controls.Camera
{
    public record OverlayBox(RectF Bounds, Color? Color, string Text, Color? CaptionColor);
    public abstract class FrameAnalyzer
    {
        readonly Dictionary<Microsoft.Maui.Controls.BindableProperty, object?> values = [];
        public bool IsEnabled { get; set; } = true;
        public bool ShowBoundingBox { get; set; }
        public abstract string Id { get; }
        public abstract ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeAsync(CameraFrame frame, CancellationToken ct);
        protected void Raise(Action action) => action();
        protected bool InScanWindow(RectF bounds) => true;
        protected object? GetValue(Microsoft.Maui.Controls.BindableProperty property) => values.GetValueOrDefault(property);
        protected void SetValue(Microsoft.Maui.Controls.BindableProperty property, object? value) => values[property] = value;
    }
}
