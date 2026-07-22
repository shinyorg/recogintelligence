using Microsoft.Maui.Controls;
using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Camera;

namespace Shiny.FaceIntelligence.Maui;

/// <summary>
/// A drop-in <b>recognition</b> camera: live preview, face detection and continuous identification behind
/// one control. It owns the <c>CameraView</c>, the camera permission, the start/stop lifecycle and the
/// <see cref="FaceRecognitionAnalyzer"/>, so a consumer writes one line of XAML and handles one event.
/// For guided, multi-shot enrollment use <see cref="FaceEnrollmentView"/> instead.
/// </summary>
/// <example>
/// <code language="xml">
/// &lt;fi:FaceRecognitionView FaceRecognized="OnFaceRecognized" /&gt;
/// </code>
/// </example>
/// <remarks>
/// <para>
/// <b>Enrollment and recognition share one pipeline.</b> <see cref="EnrollAsync"/> enrolls the frame the
/// analyzer just looked at — the same bytes, the same box, the same orientation and mirror correction that
/// recognition uses. That equality is the point, not the convenience: a template captured through a
/// different path than the probe (a separate still capture, say) differs systematically from everything it
/// is later compared against, and no threshold tuning fixes that.
/// </para>
/// <para>
/// The control also absorbs the <c>CameraView</c> handler-lifecycle trap: <c>CameraView</c> silently
/// no-ops (permission returns <c>false</c>, <c>StartAsync</c> completes doing nothing) until its handler is
/// connected, and <c>OnAppearing</c> is too early. Starting is driven from both <c>Loaded</c> and the inner
/// view's <c>HandlerChanged</c>, so consumers can't hit it.
/// </para>
/// </remarks>
public class FaceRecognitionView : ContentView
{
    readonly CameraView camera;
    FaceRecognitionAnalyzer? analyzer;
    bool started;

    public FaceRecognitionView()
    {
        this.camera = new CameraView
        {
            Facing = CameraFacing.Front,
            ScaleMode = PreviewScaleMode.AspectFill
        };
        this.Content = this.camera;

        // Whichever of these lands last actually starts the camera; StartCameraAsync is idempotent.
        this.camera.HandlerChanged += (_, _) => this.Start();
        this.Loaded += (_, _) => this.Start();
        this.Unloaded += (_, _) => this.Stop();
    }

    /// <summary>Raised on the UI thread for every recognition attempt, including a no-match.</summary>
    public event EventHandler<FaceRecognizedEventArgs>? FaceRecognized;

    /// <summary>Raised when the camera can't be started (permission refused, hardware error) with the reason.</summary>
    public event EventHandler<string>? CameraFailed;

    /// <summary>Which camera to use. Default <see cref="CameraFacing.Front"/>.</summary>
    public static readonly BindableProperty FacingProperty = BindableProperty.Create(
        nameof(Facing), typeof(CameraFacing), typeof(FaceRecognitionView), CameraFacing.Front,
        propertyChanged: (b, _, v) => ((FaceRecognitionView)b).camera.Facing = (CameraFacing)v);

    /// <inheritdoc cref="FacingProperty"/>
    public CameraFacing Facing
    {
        get => (CameraFacing)this.GetValue(FacingProperty);
        set => this.SetValue(FacingProperty, value);
    }

    /// <summary>
    /// Whether to run recognition. Default <c>true</c>. Set <c>false</c> on an enroll screen: detection keeps
    /// running (the box still draws, <see cref="EnrollAsync"/> still works) without spending an embed and a
    /// vector query per interval on frames nobody is matching.
    /// </summary>
    public static readonly BindableProperty RecognitionEnabledProperty = BindableProperty.Create(
        nameof(RecognitionEnabled), typeof(bool), typeof(FaceRecognitionView), true,
        propertyChanged: (b, _, v) =>
        {
            var view = (FaceRecognitionView)b;
            if (view.analyzer is not null)
                view.analyzer.RecognitionEnabled = (bool)v;
        });

    /// <inheritdoc cref="RecognitionEnabledProperty"/>
    public bool RecognitionEnabled
    {
        get => (bool)this.GetValue(RecognitionEnabledProperty);
        set => this.SetValue(RecognitionEnabledProperty, value);
    }

    /// <summary>Whether a face is in view right now — bind an enroll button's <c>IsEnabled</c> to it.</summary>
    public bool HasFace => this.analyzer?.LastFace is not null;

    /// <summary>
    /// The underlying analyzer once the control has resolved it, for tuning (<c>StabilityFrames</c>,
    /// <c>RecognitionInterval</c>, <c>MinConfidence</c>, box colors). <c>null</c> before the handler connects.
    /// </summary>
    public FaceRecognitionAnalyzer? Analyzer => this.analyzer;

    /// <summary>
    /// Enroll the face the analyzer is currently looking at, under <paramref name="name"/>. Returns the
    /// stored <see cref="Person"/>, or <c>null</c> when no face is currently in view.
    /// </summary>
    /// <remarks>
    /// Propagates <see cref="FaceEnrollmentConflictException"/> when this face already matches someone else
    /// (re-call with <paramref name="allowDuplicate"/> to force) and <see cref="FileNotFoundException"/> when
    /// the model is missing — handle both, they are the two a UI must explain.
    /// </remarks>
    /// <param name="name">Display name to enroll under. Enroll several shots per person, varying angle and lighting.</param>
    /// <param name="allowDuplicate">Skip the "this looks like someone else" gate.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<Person?> EnrollAsync(string name, bool allowDuplicate = false, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var intelligence = this.Resolve<IFaceIntelligence>();
        var face = this.analyzer?.LastFace;
        if (intelligence is null || face is null)
            return null;

        // The box-based overload: no re-detection, so the crop is byte-identical to what recognition sees.
        return await intelligence.Enroll(name, face.ImageData, face.Box, ct).ConfigureAwait(false);
    }

    void Start()
    {
        this.EnsureAnalyzer();
        _ = this.StartCameraAsync();
    }

    void EnsureAnalyzer()
    {
        if (this.analyzer is not null)
            return;

        this.analyzer = this.Resolve<FaceRecognitionAnalyzer>();
        if (this.analyzer is null)
            return;

        this.analyzer.RecognitionEnabled = this.RecognitionEnabled;
        this.analyzer.FaceRecognized += (s, e) => this.FaceRecognized?.Invoke(this, e);
        this.camera.Analyzer = this.analyzer;
    }

    async Task StartCameraAsync()
    {
        // Handler null => every CameraView call silently no-ops; wait for HandlerChanged to fire instead.
        if (this.started || this.camera.Handler is null)
            return;

        this.started = true;
        try
        {
            if (!await this.camera.RequestPermissionAsync())
            {
                this.started = false;
                this.CameraFailed?.Invoke(this, "Camera permission denied.");
                return;
            }
            await this.camera.StartAsync();
        }
        catch (Exception ex)
        {
            this.started = false;
            this.CameraFailed?.Invoke(this, $"Camera start failed ({ex.GetType().Name}): {ex.Message}");
        }
    }

    async void Stop()
    {
        if (!this.started)
            return;
        this.started = false;
        try
        {
            await this.camera.StopAsync();
        }
        catch
        {
            // Tearing down a camera that's already gone isn't worth surfacing.
        }
    }

    /// <summary>
    /// Reach the app's service provider through the handler's <c>MauiContext</c> — the standard way for a
    /// control to resolve services without the consumer having to inject anything into it.
    /// </summary>
    T? Resolve<T>() where T : class
    {
        var services = this.Handler?.MauiContext?.Services ?? this.camera.Handler?.MauiContext?.Services;
        return services?.GetService(typeof(T)) as T;
    }
}
