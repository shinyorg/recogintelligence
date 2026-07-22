using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Camera;

namespace Shiny.FaceIntelligence.Maui;

/// <summary>
/// Guided face enrollment: walks a person through a sequence of prompts, captures one shot per step, and
/// enrolls them all under a name. Camera, permission, lifecycle, detection, quality gating and the on-screen
/// overlay are all inside the control.
/// </summary>
/// <example>
/// <code language="xml">
/// &lt;fi:FaceEnrollmentView PersonName="{Binding Name}" Completed="OnEnrolled" /&gt;
/// </code>
/// </example>
/// <remarks>
/// <para>
/// The counterpart to <see cref="FaceRecognitionView"/>, and deliberately a separate control: enrollment
/// wants <i>diverse</i> captures and has steps, progress and instructions; recognition wants one fast
/// confident answer and has none of that.
/// </para>
/// <para>
/// <b>What it can and cannot check.</b> A prompt like "turn your head slightly left" cannot be verified —
/// the detector returns a box and a confidence, not landmarks or pose. So the gates are the measurable
/// ones: face size within the step's range, steadiness, sharpness, exposure, and — the important one —
/// whether the shot's embedding is far enough from the ones already captured. Novelty is the check that
/// actually delivers a varied gallery, because embedding spread <i>is</i> the goal; asking someone to turn
/// their head is only a means to it.
/// </para>
/// <para>
/// Shots are enrolled with the box-based overload on the analyzed frame, so templates go through exactly
/// the same preprocessing as recognition probes.
/// </para>
/// </remarks>
public class FaceEnrollmentView : ContentView
{
    readonly CameraView camera;
    readonly Label instructionLabel;
    readonly Label hintLabel;
    readonly Label progressLabel;

    readonly List<AnalyzedFace> captured = [];
    readonly List<float[]> capturedEmbeddings = [];
    readonly List<Person> people = [];

    FaceRecognitionAnalyzer? analyzer;
    IFaceEmbedder? embedder;
    IFaceIntelligence? intelligence;
    bool started;
    bool running;
    bool evaluating;
    int stepIndex;

    IDispatcherTimer? countdownTimer;
    int countdownRemaining;
    bool armed;
    DateTimeOffset armedAt = DateTimeOffset.MaxValue;

    public FaceEnrollmentView()
    {
        this.camera = new CameraView
        {
            Facing = CameraFacing.Front,
            ScaleMode = PreviewScaleMode.AspectFill
        };

        this.instructionLabel = new Label
        {
            TextColor = Colors.White,
            FontSize = 20,
            HorizontalTextAlignment = TextAlignment.Center
        };
        this.hintLabel = new Label
        {
            TextColor = Color.FromArgb("#FFD27F"),
            FontSize = 13,
            HorizontalTextAlignment = TextAlignment.Center
        };
        this.progressLabel = new Label
        {
            TextColor = Color.FromArgb("#99FFFFFF"),
            FontSize = 12,
            HorizontalTextAlignment = TextAlignment.Center
        };

        var banner = new Border
        {
            BackgroundColor = Color.FromArgb("#CC000000"),
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(16, 12),
            Margin = new Thickness(16),
            VerticalOptions = LayoutOptions.End,
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children = { this.instructionLabel, this.hintLabel, this.progressLabel }
            }
        };

        this.Content = new Grid { Children = { this.camera, banner } };

        this.camera.HandlerChanged += (_, _) => this.Start();
        this.Loaded += (_, _) => this.Start();
        this.Unloaded += (_, _) => this.Stop();
    }

    /// <summary>Name to enroll the captured shots under. Enrollment won't start until this is set.</summary>
    public static readonly BindableProperty PersonNameProperty = BindableProperty.Create(
        nameof(PersonName), typeof(string), typeof(FaceEnrollmentView));

    /// <inheritdoc cref="PersonNameProperty"/>
    public string? PersonName
    {
        get => (string?)this.GetValue(PersonNameProperty);
        set => this.SetValue(PersonNameProperty, value);
    }

    /// <summary>The prompt sequence. Defaults to <see cref="FaceEnrollmentStep.Default"/>.</summary>
    public IReadOnlyList<FaceEnrollmentStep> Steps { get; set; } = FaceEnrollmentStep.Default;

    /// <summary>
    /// How far a new shot's embedding must be from every shot already captured, in cosine distance.
    /// Default 0.18.
    /// </summary>
    /// <remarks>
    /// This has to clear the model's natural frame-to-frame jitter, or every frame reads as "novel" and the
    /// whole sequence completes in a few frames. Measured on-device, the <b>same</b> face a few seconds
    /// apart spans roughly 0.08–0.53, so a threshold below ~0.1 is inside the noise. 0.18 asks for a change
    /// clearly larger than jitter without demanding a pose the person can't hold. Raise it to force more
    /// pronounced changes; lower it if people get stuck on a step.
    /// </remarks>
    public float MinNoveltyDistance { get; set; } = 0.18f;

    /// <summary>
    /// Countdown shown at the start of each step before captures are accepted. Default 3 s.
    /// </summary>
    /// <remarks>
    /// A wizard that captures faster than a person can read the instruction isn't guiding anything — it just
    /// takes N shots of whatever was already in frame. The countdown is deliberately <b>visible</b> ("Get
    /// ready… 3, 2, 1" then "Hold still…") rather than a silent delay, so the person knows they have time to
    /// move and when the shot is actually coming. That matters more here than in most wizards, because pose
    /// can't be verified — the prompt plus the pause <i>is</i> the mechanism.
    /// </remarks>
    public TimeSpan StepCountdown { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long a step may go unsatisfied before it is skipped. Default 12 s (measured from the end of the
    /// countdown). Set <see cref="TimeSpan.Zero"/> to never skip.
    /// </summary>
    /// <remarks>
    /// Some steps are simply unreachable for some people: "move back a little" caps the face at 30% of the
    /// frame, which an arm's length away on a phone may never satisfy. Without a timeout the sequence stalls
    /// on that step forever. Skipping keeps the shots already captured — which, combined with storing each
    /// shot as it is accepted, means an awkward step costs one shot rather than the whole session.
    /// </remarks>
    public TimeSpan StepTimeout { get; set; } = TimeSpan.FromSeconds(12);

    /// <summary>Minimum sharpness (variance of Laplacian, scaled). Default 8. Below this, the shot is blurry.</summary>
    public float MinSharpness { get; set; } = 8f;

    /// <summary>Acceptable mean brightness of the face crop, 0..1. Defaults to 0.18–0.92.</summary>
    public float MinBrightness { get; set; } = 0.18f;

    /// <inheritdoc cref="MinBrightness"/>
    public float MaxBrightness { get; set; } = 0.92f;

    /// <summary>How many consecutive frames the face must hold steady before a shot is considered.</summary>
    public int RequiredStableFrames { get; set; } = 3;

    /// <summary>Raised when the current step or the captured count changes.</summary>
    public event EventHandler<FaceEnrollmentProgress>? Progress;

    /// <summary>Raised when a candidate frame is turned down, with a ready-to-show hint.</summary>
    public event EventHandler<FaceEnrollmentRejected>? Rejected;

    /// <summary>Raised once every step is done and all shots have been enrolled.</summary>
    public event EventHandler<FaceEnrollmentResult>? Completed;

    /// <summary>Raised when the camera can't be started, with the reason.</summary>
    public event EventHandler<string>? CameraFailed;

    /// <summary>True while a sequence is in progress.</summary>
    public bool IsRunning => this.running;

    /// <summary>
    /// Begin (or restart) the guided sequence. Requires <see cref="PersonName"/>; throws if it's blank.
    /// </summary>
    public void BeginEnrollment()
    {
        if (String.IsNullOrWhiteSpace(this.PersonName))
            throw new InvalidOperationException($"Set {nameof(this.PersonName)} before starting enrollment.");
        if (this.Steps.Count == 0)
            throw new InvalidOperationException("The enrollment sequence has no steps.");

        this.captured.Clear();
        this.capturedEmbeddings.Clear();
        this.people.Clear();
        this.stepIndex = 0;
        this.running = true;
        this.BeginStep();
    }

    /// <summary>Show the step's prompt and run the countdown; captures are only accepted once it finishes.</summary>
    void BeginStep()
    {
        this.armed = false;
        this.countdownRemaining = Math.Max(1, (int)Math.Ceiling(this.StepCountdown.TotalSeconds));
        this.ReportProgress();
        this.hintLabel.Text = $"Get ready… {this.countdownRemaining}";

        this.countdownTimer ??= this.CreateCountdownTimer();
        this.countdownTimer.Stop();
        this.countdownTimer.Start();
    }

    IDispatcherTimer CreateCountdownTimer()
    {
        var timer = this.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) =>
        {
            this.countdownRemaining--;
            if (this.countdownRemaining > 0)
            {
                this.hintLabel.Text = $"Get ready… {this.countdownRemaining}";
                return;
            }

            timer.Stop();
            this.armed = true;
            this.armedAt = DateTimeOffset.UtcNow;
            this.hintLabel.Text = "Hold still…";
        };
        return timer;
    }

    /// <summary>Give up on the current step and move to the next (or finish).</summary>
    void SkipStep()
    {
        this.stepIndex++;
        this.armed = false;
        if (this.stepIndex >= this.Steps.Count)
            this.Finish();
        else
            this.BeginStep();
    }

    /// <summary>
    /// Stop the sequence. Shots already accepted have <b>already been stored</b> — enrollment is incremental
    /// so a partial run still leaves a usable gallery. Use <c>IFaceIntelligence.Forget</c> to undo.
    /// </summary>
    public void CancelEnrollment()
    {
        this.running = false;
        this.armed = false;
        this.countdownTimer?.Stop();
        this.captured.Clear();
        this.capturedEmbeddings.Clear();
        this.instructionLabel.Text = String.Empty;
        this.hintLabel.Text = String.Empty;
        this.progressLabel.Text = String.Empty;
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
        this.embedder = this.Resolve<IFaceEmbedder>();
        this.intelligence = this.Resolve<IFaceIntelligence>();
        if (this.analyzer is null)
            return;

        // Detection only — matching every couple of seconds would burn an embed and a vector query on
        // frames nobody is identifying. The wizard does its own embedding, for the novelty gate.
        Console.WriteLine(
            $"[Enroll] analyzer={(this.analyzer is null ? "NULL" : "ok")} " +
            $"embedder={(this.embedder is null ? "NULL - novelty gate disabled!" : "ok")} " +
            $"intelligence={(this.intelligence is null ? "NULL - nothing can be stored!" : "ok")}");
        this.analyzer.RecognitionEnabled = false;
        this.analyzer.FaceDetected += this.OnFaceDetected;
        this.analyzer.FaceLost += (_, _) => this.SetHint(FaceEnrollmentRejection.NoFace);
        this.camera.Analyzer = this.analyzer;
    }

    async Task StartCameraAsync()
    {
        // Handler null => every CameraView call silently no-ops; HandlerChanged brings us back here.
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
            // Stopping a camera that's already gone isn't worth surfacing.
        }
    }

    void OnFaceDetected(object? sender, AnalyzedFace face)
    {
        if (!this.running || this.evaluating || this.stepIndex >= this.Steps.Count)
            return;

        // The countdown must have finished — until then the person is still reading and moving.
        if (!this.armed)
            return;

        // A step nobody can satisfy must not stall the sequence; move on and keep what we already have.
        if (this.StepTimeout > TimeSpan.Zero && DateTimeOffset.UtcNow - this.armedAt > this.StepTimeout)
        {
            Console.WriteLine($"[Enroll] step {this.stepIndex + 1} timed out — skipping");
            this.SkipStep();
            return;
        }

        var step = this.Steps[this.stepIndex];

        // Cheap geometric gates first — these run per frame and must not touch the image.
        var fraction = FaceFraction(face);
        if (fraction < step.MinFaceFraction)
        {
            this.SetHint(FaceEnrollmentRejection.TooFar);
            return;
        }
        if (fraction > step.MaxFaceFraction)
        {
            this.SetHint(FaceEnrollmentRejection.TooClose);
            return;
        }
        if (face.StableFrames < this.RequiredStableFrames)
        {
            this.SetHint(FaceEnrollmentRejection.NotSteady);
            return;
        }

        // Everything past here decodes and embeds, so it runs off the UI thread, one at a time.
        this.evaluating = true;
        _ = this.EvaluateAsync(face, step);
    }

    async Task EvaluateAsync(AnalyzedFace face, FaceEnrollmentStep step)
    {
        try
        {
            var verdict = await Task.Run(() =>
            {
                var (sharpness, brightness) = FrameQuality.Measure(face.ImageData, face.Box);
                if (sharpness < this.MinSharpness)
                    return (Reason: (FaceEnrollmentRejection?)FaceEnrollmentRejection.TooBlurry, Embedding: (float[]?)null);
                if (brightness < this.MinBrightness)
                    return (FaceEnrollmentRejection.TooDark, null);
                if (brightness > this.MaxBrightness)
                    return (FaceEnrollmentRejection.TooBright, null);

                if (this.embedder is null)
                {
                    Console.WriteLine("[Enroll] no IFaceEmbedder — accepting on geometry+quality only");
                    return (null, null);
                }

                var embedding = this.embedder.Embed(face.ImageData, face.Box).ToArray();
                if (step.RequireNovelty && this.capturedEmbeddings.Count > 0)
                {
                    var nearest = this.capturedEmbeddings.Min(e => Distance(e, embedding));
                    Console.WriteLine($"[Enroll] step {this.stepIndex + 1}: sharp {sharpness:F1} bright {brightness:F2} nearest {nearest:F4} (need >= {this.MinNoveltyDistance:F2})");
                    if (nearest < this.MinNoveltyDistance)
                        return (FaceEnrollmentRejection.TooSimilar, null);
                }
                else
                {
                    Console.WriteLine($"[Enroll] step {this.stepIndex + 1}: sharp {sharpness:F1} bright {brightness:F2} (first shot, no novelty check)");
                }
                return (null, embedding);
            });

            if (verdict.Reason is { } reason)
            {
                this.SetHint(reason);
                return;
            }

            Console.WriteLine($"[Enroll] ACCEPTED step {this.stepIndex + 1} ({this.captured.Count + 1} captured)");
            this.captured.Add(face);
            if (verdict.Embedding is { } vec)
                this.capturedEmbeddings.Add(vec);

            // Store immediately rather than batching to the end. Deferring meant a single unsatisfiable step
            // threw away every shot already captured — the person stands there completing five prompts and
            // ends up with nothing enrolled. Incremental storage makes a partial session still useful.
            if (!await this.StoreAsync(face))
                return;

            this.stepIndex++;
            this.armed = false;

            if (this.stepIndex >= this.Steps.Count)
                this.Finish();
            else
                this.BeginStep();
        }
        catch (Exception ex)
        {
            this.running = false;
            this.CameraFailed?.Invoke(this, $"Enrollment failed ({ex.GetType().Name}): {ex.Message}");
        }
        finally
        {
            this.evaluating = false;
        }
    }

    /// <summary>Store one accepted shot straight away. Returns false (and reports) if it couldn't be stored.</summary>
    async Task<bool> StoreAsync(AnalyzedFace shot)
    {
        var name = this.PersonName?.Trim();
        if (String.IsNullOrWhiteSpace(name))
        {
            this.Fail("Nothing stored — PersonName is empty.");
            return false;
        }

        // Resolved up front with the analyzer; re-resolve as a fallback in case the handler wasn't ready then.
        var store = this.intelligence ??= this.Resolve<IFaceIntelligence>();
        if (store is null)
        {
            this.Fail("Nothing stored — no IFaceIntelligence is registered. Call AddFaceIntelligence(...) at startup.");
            return false;
        }

        try
        {
            // The box-based overload: no re-detection, and no duplicate gate — the sequence is explicitly one
            // person, so shots 2..n are *expected* to match shot 1.
            this.people.Add(await store.Enroll(name, shot.ImageData, shot.Box));
            Console.WriteLine($"[Enroll] stored shot {this.people.Count} for '{name}'");
            return true;
        }
        catch (Exception ex)
        {
            this.Fail($"Stored {this.people.Count} shot(s), then failed ({ex.GetType().Name}): {ex.Message}");
            return false;
        }
    }

    void Finish()
    {
        this.running = false;
        this.armed = false;
        this.countdownTimer?.Stop();

        var minPair = 1f;
        for (var i = 0; i < this.capturedEmbeddings.Count; i++)
            for (var j = i + 1; j < this.capturedEmbeddings.Count; j++)
                minPair = MathF.Min(minPair, Distance(this.capturedEmbeddings[i], this.capturedEmbeddings[j]));

        var name = this.PersonName?.Trim() ?? String.Empty;
        var skipped = this.Steps.Count - this.captured.Count;
        Console.WriteLine($"[Enroll] done: {this.people.Count} stored, {skipped} skipped, min pairwise {minPair:F3}");

        this.instructionLabel.Text = $"Enrolled {this.people.Count} shots for {name}";
        this.hintLabel.Text = skipped > 0 ? $"{skipped} step(s) skipped" : String.Empty;
        this.Completed?.Invoke(this, new FaceEnrollmentResult(
            name, this.people.ToList(), this.capturedEmbeddings.Count > 1 ? minPair : 0f, skipped));
    }

    /// <summary>Surface a failure rather than ending the sequence quietly with nothing saved.</summary>
    void Fail(string message)
    {
        Console.WriteLine($"[Enroll] {message}");
        this.instructionLabel.Text = "Enrollment failed";
        this.hintLabel.Text = message;
        this.CameraFailed?.Invoke(this, message);
    }

    void ReportProgress()
    {
        if (this.stepIndex < this.Steps.Count)
        {
            var step = this.Steps[this.stepIndex];
            this.instructionLabel.Text = step.Instruction;
            this.progressLabel.Text = $"Step {this.stepIndex + 1} of {this.Steps.Count} · {this.captured.Count} captured";
            this.Progress?.Invoke(this, new FaceEnrollmentProgress(
                this.stepIndex, this.Steps.Count, step.Instruction, this.captured.Count));
        }
        else
        {
            this.progressLabel.Text = $"{this.captured.Count} captured";
        }
    }

    void SetHint(FaceEnrollmentRejection reason)
    {
        var hint = reason switch
        {
            FaceEnrollmentRejection.NoFace => "No face detected — move into the frame.",
            FaceEnrollmentRejection.TooFar => "Move closer to the camera.",
            FaceEnrollmentRejection.TooClose => "Move back a little.",
            FaceEnrollmentRejection.NotSteady => "Hold still…",
            FaceEnrollmentRejection.TooBlurry => "Too blurry — hold still and let the camera focus.",
            FaceEnrollmentRejection.TooDark => "Too dark — find better light.",
            FaceEnrollmentRejection.TooBright => "Too bright — move away from the light behind you.",
            FaceEnrollmentRejection.TooSimilar => "That looked like the last shot — change your pose a little more.",
            _ => String.Empty
        };

        if (this.hintLabel.Text != hint)
        {
            this.hintLabel.Text = hint;
            this.Rejected?.Invoke(this, new FaceEnrollmentRejected(reason, hint));
        }
    }

    /// <summary>Face size as a fraction of the frame's shorter side — the scale-independent "how close" measure.</summary>
    static float FaceFraction(AnalyzedFace face)
        => MathF.Max(face.Bounds.Width, face.Bounds.Height);

    static float Distance(float[] a, float[] b)
    {
        var dot = 0f;
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
            dot += a[i] * b[i];
        return 1f - dot;
    }

    /// <summary>Reach the app's services through the handler's MauiContext — no injection needed by consumers.</summary>
    T? Resolve<T>() where T : class
    {
        var services = this.Handler?.MauiContext?.Services ?? this.camera.Handler?.MauiContext?.Services;
        return services?.GetService(typeof(T)) as T;
    }
}
