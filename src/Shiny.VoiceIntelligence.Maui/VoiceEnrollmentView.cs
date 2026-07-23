using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Shiny.VoiceIntelligence.Maui;

/// <summary>
/// Guided voice enrollment: shows the sentences to read, records them one after another, and keeps going
/// until the stored voiceprints agree closely enough to be worth matching against.
/// </summary>
/// <example>
/// <code language="xml">
/// &lt;vi:VoiceEnrollmentView PersonIdentifier="{Binding Name}" Completed="OnEnrolled" /&gt;
/// </code>
/// </example>
/// <remarks>
/// <para>
/// The control is a <b>driver for <see cref="VoiceEnrollmentSession"/></b>, which holds all the judgement —
/// what makes a recording usable, whether the set agrees, when to stop. Nothing here decides quality; if you
/// want the same behaviour without a UI (server-side, or a different toolkit), drive the session directly.
/// </para>
/// <para>
/// <b>The gate is inverted compared with face enrollment.</b> The face wizard wants <i>spread</i> — varied
/// poses — so it rejects shots resembling ones it already has. A speaker embedding is supposed to be the
/// same whatever the person says, so this rejects recordings that <i>disagree</i>. Agreement is both the
/// quality gate and the stop condition, which is why the run has no fixed length: it ends when the clips
/// agree, not when the sentences run out.
/// </para>
/// <para>
/// <b>Sentences are not passphrases.</b> The model is text-independent and nothing verifies that the
/// sentence was read — that would need speech-to-text. They are shown because phonetically rich sentences
/// exercise more of the vocal tract, and because having something to read keeps a person talking for the
/// whole window instead of trailing off after two seconds.
/// </para>
/// <para>
/// <b>Audio comes from the app</b>, through <see cref="IVoiceRecorder"/> — see that interface for why.
/// Register one, plus <c>AddVoiceIntelligence(...)</c>, and the control resolves both itself.
/// </para>
/// </remarks>
public class VoiceEnrollmentView : ContentView
{
    static readonly Color AcceptedColor = Color.FromArgb("#2E7D32");
    static readonly Color CurrentColor = Color.FromArgb("#1565C0");
    static readonly Color PendingColor = Color.FromArgb("#9E9E9E");
    static readonly Color WarningColor = Color.FromArgb("#C62828");

    readonly Label headlineLabel;
    readonly Label statusLabel;
    readonly ProgressBar recordProgress;
    readonly GraphicsView vuMeter;
    readonly VuMeterDrawable vuDrawable = new();
    readonly IProgress<float> levelProgress;
    readonly Label hintLabel;
    readonly Label progressLabel;
    readonly VerticalStackLayout promptList;
    readonly List<Label> promptGlyphs = [];
    readonly List<Label> promptTexts = [];
    readonly HashSet<int> acceptedPrompts = [];

    VoiceEnrollmentSession? session;
    CancellationTokenSource? cts;
    int currentPrompt = -1;

    // Bumped on every start/stop. A cancelled loop is still parked inside RecordAsync for up to RecordFor,
    // so without this it wakes up afterwards and writes its prompt index, its ticks and its status over the
    // run that replaced it — which looks exactly like the wizard losing track of where it is.
    int runGeneration;

    /// <summary>Creates the control and its layout.</summary>
    public VoiceEnrollmentView()
    {
        this.headlineLabel = new Label { FontSize = 17, FontAttributes = FontAttributes.Bold };
        // Large and centred: at 15pt inline this was missed entirely, and a countdown nobody notices is a
        // countdown that doesn't do its job — people start reading late and the clip gets rejected.
        this.statusLabel = new Label
        {
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Fill
        };
        this.recordProgress = new ProgressBar { IsVisible = false };
        this.vuMeter = new GraphicsView { Drawable = this.vuDrawable, HeightRequest = 18, IsVisible = false };

        // Constructed on the UI thread, so Progress<T> marshals the recorder's callbacks back to it and the
        // recorder can report from whatever thread its capture loop runs on.
        this.levelProgress = new Progress<float>(this.OnLevel);
        this.hintLabel = new Label { FontSize = 13, TextColor = WarningColor, LineBreakMode = LineBreakMode.WordWrap };
        this.progressLabel = new Label { FontSize = 13, TextColor = PendingColor };
        this.promptList = new VerticalStackLayout { Spacing = 8 };

        this.Content = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                this.headlineLabel,
                new Border
                {
                    Stroke = Color.FromArgb("#22000000"),
                    StrokeShape = new RoundRectangle { CornerRadius = 8 },
                    Padding = 12,
                    Content = this.promptList
                },
                this.statusLabel,
                this.vuMeter,
                this.recordProgress,
                this.hintLabel,
                this.progressLabel
            }
        };

        // Show the sentences straight away rather than waiting for BeginEnrollment: someone should be able
        // to read what they're about to be asked to say before committing to it. ForThreshold only adjusts
        // the distance gates, so these are the same prompts the session will actually use.
        this.BuildPromptList(new VoiceEnrollmentOptions().Prompts);
        this.ShowIdle();
    }

    /// <summary>
    /// Identity to enroll the recordings under — an opaque caller-chosen key, see
    /// <see cref="Speaker.PersonIdentifier"/>. Enrollment won't start until this is set.
    /// </summary>
    public static readonly BindableProperty PersonIdentifierProperty = BindableProperty.Create(
        nameof(PersonIdentifier), typeof(string), typeof(VoiceEnrollmentView));

    /// <inheritdoc cref="PersonIdentifierProperty"/>
    public string? PersonIdentifier
    {
        get => (string?)this.GetValue(PersonIdentifierProperty);
        set => this.SetValue(PersonIdentifierProperty, value);
    }

    /// <summary>
    /// Prompts and gates. Leave null to derive them from the recognizer's matching threshold, which is
    /// almost always what you want — see <see cref="VoiceEnrollmentOptions.ForThreshold"/>.
    /// </summary>
    public VoiceEnrollmentOptions? Options
    {
        get => this.options;
        set
        {
            this.options = value;
            // Keep the visible list honest before a run starts.
            this.BuildPromptList((value ?? new VoiceEnrollmentOptions()).Prompts);
        }
    }
    VoiceEnrollmentOptions? options;

    /// <summary>How long each recording runs. Default 5 s.</summary>
    /// <remarks>
    /// Use the same value for recognition. A pooled speaker model is largely length-invariant, but there is
    /// no reason to introduce a systematic difference between template and probe.
    /// </remarks>
    public TimeSpan RecordFor { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Visible "get ready" countdown before each recording. Default 3 s.
    /// </summary>
    /// <remarks>
    /// Not cosmetic: recording the instant a sentence appears captures someone still reading it, and the
    /// resulting near-silent clip is exactly what the session then rejects.
    /// </remarks>
    public TimeSpan Countdown { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Hard ceiling on how many recordings to ask for, accepted or not. Default 12.
    /// </summary>
    /// <remarks>
    /// The session's own stop condition is agreement, not a count, so a bad room can reject clips forever.
    /// This is the backstop. On hitting it the control calls <see cref="VoiceEnrollmentSession.Finish"/>,
    /// which stores the best subset flagged <c>IsConfident=false</c> rather than throwing the run away —
    /// unless fewer than <see cref="VoiceEnrollmentOptions.MinSamples"/> were ever accepted, in which case
    /// there is nothing worth storing and <see cref="Failed"/> is raised.
    /// <para>
    /// When <see cref="Options"/> is left null the control also raises the session's
    /// <see cref="VoiceEnrollmentOptions.MaxSamples"/> to this value, so a struggling run keeps trying
    /// instead of settling at the default six. Supply <see cref="Options"/> to control that yourself.
    /// </para>
    /// </remarks>
    public int MaxAttempts { get; set; } = 12;

    /// <summary>True while a run is in progress.</summary>
    public bool IsRunning => this.cts is not null;

    /// <summary>The live session, or null before the first run. Exposed for diagnostics.</summary>
    public VoiceEnrollmentSession? Session => this.session;

    /// <summary>Raised once the recordings have been stored. Carries the cohesion and the confidence flag.</summary>
    public event EventHandler<VoiceEnrollmentResult>? Completed;

    /// <summary>Raised when the run could not produce an enrollment. The string is ready to show.</summary>
    public event EventHandler<string>? Failed;

    /// <summary>Raised after every recording, accepted or not. For logging and custom progress UI.</summary>
    public event EventHandler<VoiceEnrollmentStepResult>? StepCompleted;

    /// <summary>
    /// Begin (or restart) the guided run. Requires <see cref="PersonIdentifier"/>; throws if it's blank.
    /// Everything after this is automatic — the control records, checks, and repeats until it's satisfied.
    /// </summary>
    public void BeginEnrollment()
    {
        if (String.IsNullOrWhiteSpace(this.PersonIdentifier))
            throw new InvalidOperationException($"Set {nameof(this.PersonIdentifier)} before starting enrollment.");

        this.Stop(showIdle: false);

        var voice = this.Resolve<IVoiceIntelligence>();
        if (voice is null)
        {
            this.Fail("No IVoiceIntelligence is registered. Call AddVoiceIntelligence(...) at startup.");
            return;
        }

        var recorder = this.Resolve<IVoiceRecorder>();
        if (recorder is null)
        {
            this.Fail($"No {nameof(IVoiceRecorder)} is registered — the app supplies audio capture. Register one at startup.");
            return;
        }

        this.session = voice.CreateEnrollment(this.PersonIdentifier!, this.options);

        // Only when we built the options: a caller who supplied their own MaxSamples meant it.
        if (this.options is null)
            this.session.Options.MaxSamples = Math.Max(this.session.Options.MaxSamples, this.MaxAttempts);

        this.BuildPromptList(this.session.Options.Prompts);
        this.acceptedPrompts.Clear();
        this.cts = new CancellationTokenSource();
        _ = this.RunAsync(this.session, recorder, ++this.runGeneration, this.cts.Token);
    }

    /// <summary>
    /// Abandon the run. Nothing is stored — the session writes only on completion — so this leaves no
    /// half-enrolled speaker behind.
    /// </summary>
    public void CancelEnrollment() => this.Stop(showIdle: true);

    void Stop(bool showIdle)
    {
        this.runGeneration++;
        this.cts?.Cancel();
        this.cts?.Dispose();
        this.cts = null;
        if (showIdle)
            this.ShowIdle();
    }

    /// <inheritdoc/>
    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        base.OnHandlerChanging(args);
        // Leaving the tree mid-run: stop recording rather than hold the mic open behind a dead page. No
        // ShowIdle — the control is on its way out and its labels no longer have anywhere to render.
        if (args.NewHandler is null)
            this.Stop(showIdle: false);
    }

    async Task RunAsync(VoiceEnrollmentSession active, IVoiceRecorder recorder, int generation, CancellationToken ct)
    {
        var lastRejected = false;
        // True once a newer run (or a cancel) has taken over: this loop must then touch nothing.
        bool Stale() => generation != this.runGeneration;

        try
        {
            while (!active.IsComplete && active.AttemptCount < this.MaxAttempts)
            {
                ct.ThrowIfCancellationRequested();

                // The session owns which sentence is next, and it stays put until a recording is accepted.
                this.currentPrompt = active.CurrentPromptIndex;
                this.RefreshPrompts();
                this.headlineLabel.Text = lastRejected ? "Once more, same sentence" : "Read this sentence aloud";

                await this.RunCountdownAsync(ct);
                if (Stale())
                    return;

                this.SetStatus("● Recording", WarningColor, 24);
                this.vuDrawable.Threshold = active.Options.MinSpeechLevel;
                this.vuDrawable.IsActive = true;
                this.vuMeter.IsVisible = true;
                this.recordProgress.Progress = 0;
                this.recordProgress.IsVisible = true;
                // Fire-and-forget animation: "Recording" for five silent seconds gives no sense of how much
                // longer to keep talking, and trailing off early is one of the rejections we then show.
                _ = this.recordProgress.ProgressTo(1d, (uint)this.RecordFor.TotalMilliseconds, Easing.Linear);

                var samples = await recorder.RecordAsync(this.RecordFor, this.levelProgress, ct);

                ct.ThrowIfCancellationRequested();
                if (Stale())
                    return;

                this.StopMeter();
                this.recordProgress.IsVisible = false;
                this.SetStatus("Checking…", CurrentColor, 17);
                var step = await active.Submit(samples, ct);
                if (Stale())
                    return;

                lastRejected = !step.Accepted;
                if (step.Accepted)
                    this.acceptedPrompts.Add(this.currentPrompt);

                this.hintLabel.Text = step.Hint;
                this.RefreshPrompts();
                this.ShowProgress(active);
                this.StepCompleted?.Invoke(this, step);
            }

            // Out of attempts without agreement: keep the best of what we got rather than discard the lot.
            var result = active.Result ?? await active.Finish(ct);
            if (Stale())
                return;

            if (result is null)
            {
                this.Fail(
                    $"Couldn't get {active.Options.MinSamples} usable recordings in {active.AttemptCount} tries. " +
                    "Try somewhere quieter, and keep talking for the whole countdown.");
                return;
            }

            this.Complete(result);
        }
        catch (OperationCanceledException)
        {
            // Stop() already reset the UI (or a newer run owns it now).
        }
        catch (Exception ex) when (!Stale())
        {
            this.Fail($"Enrollment failed ({ex.GetType().Name}): {ex.Message}");
        }
        catch
        {
            // Superseded run — its failure is no longer anyone's business.
        }
        finally
        {
            if (this.cts is { } source && source.Token == ct)
            {
                source.Dispose();
                this.cts = null;
            }
        }
    }

    async Task RunCountdownAsync(CancellationToken ct)
    {
        this.recordProgress.IsVisible = false;
        var remaining = (int)Math.Ceiling(this.Countdown.TotalSeconds);
        for (; remaining > 0; remaining--)
        {
                this.SetStatus($"Get ready… {remaining}", CurrentColor, 34);
            await Task.Delay(1000, ct);
        }
    }

    void BuildPromptList(IReadOnlyList<string> prompts)
    {
        this.promptList.Clear();
        this.promptGlyphs.Clear();
        this.promptTexts.Clear();

        foreach (var prompt in prompts)
        {
            var glyph = new Label { FontSize = 14, TextColor = PendingColor, WidthRequest = 20, VerticalOptions = LayoutOptions.Start };
            var text = new Label { FontSize = 14, TextColor = PendingColor, LineBreakMode = LineBreakMode.WordWrap, Text = prompt };
            this.promptGlyphs.Add(glyph);
            this.promptTexts.Add(text);

            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 8 };
            row.Add(glyph, 0);
            row.Add(text, 1);
            this.promptList.Add(row);
        }
        this.RefreshPrompts();
    }

    /// <summary>
    /// Mark each sentence accepted / current / pending. A tick means "a recording of this sentence was
    /// kept" — sentences rotate per <i>attempt</i>, so a rejected one moves on without a tick and comes
    /// round again if the run goes long.
    /// </summary>
    void RefreshPrompts()
    {
        for (var i = 0; i < this.promptGlyphs.Count; i++)
        {
            var (glyph, color) =
                this.acceptedPrompts.Contains(i) ? ("✓", AcceptedColor) :
                i == this.currentPrompt ? ("▶", CurrentColor) :
                ("○", PendingColor);

            this.promptGlyphs[i].Text = glyph;
            this.promptGlyphs[i].TextColor = color;
            this.promptTexts[i].TextColor = color;
            this.promptTexts[i].FontAttributes = i == this.currentPrompt ? FontAttributes.Bold : FontAttributes.None;
        }
    }

    /// <summary>
    /// Fast attack, slow release — the standard meter ballistics. Reporting the raw chunk RMS makes the bar
    /// flicker on every syllable gap and reads as a fault; holding the decay makes it track the voice.
    /// </summary>
    void OnLevel(float rms)
    {
        if (!this.vuDrawable.IsActive)
            return;

        this.vuDrawable.Level = MathF.Max(rms, this.vuDrawable.Level * 0.72f);
        this.vuDrawable.Peak = MathF.Max(rms, this.vuDrawable.Peak * 0.94f);
        this.vuMeter.Invalidate();
    }

    void StopMeter()
    {
        this.vuDrawable.IsActive = false;
        this.vuDrawable.Level = 0f;
        this.vuDrawable.Peak = 0f;
        this.vuMeter.IsVisible = false;
        this.vuMeter.Invalidate();
    }

    /// <summary>
    /// One status line, sized to its job: the countdown is the only thing on screen that has to be caught
    /// out of the corner of an eye while reading, so it gets to be big — the resting text does not.
    /// </summary>
    void SetStatus(string text, Color color, double fontSize)
    {
        this.statusLabel.Text = text;
        this.statusLabel.TextColor = color;
        this.statusLabel.FontSize = fontSize;
    }

    void ShowProgress(VoiceEnrollmentSession active)
    {
        var agreement = active.AcceptedCount > 1 ? $" · agreement {active.Cohesion:F2}" : String.Empty;
        this.progressLabel.Text =
            $"{active.AcceptedCount} of {active.Options.MinSamples} kept · {active.AttemptCount} of {this.MaxAttempts} tries{agreement}";
    }

    void ShowIdle()
    {
        this.recordProgress.IsVisible = false;
        this.StopMeter();
        this.headlineLabel.Text = "Voice enrollment";
        this.SetStatus("Ready when you are.", PendingColor, 15);
        this.hintLabel.Text = String.Empty;
        this.progressLabel.Text = String.Empty;
        this.currentPrompt = -1;
        this.RefreshPrompts();
    }

    void Complete(VoiceEnrollmentResult result)
    {
        this.recordProgress.IsVisible = false;
        this.StopMeter();
        this.currentPrompt = -1;
        this.RefreshPrompts();
        this.headlineLabel.Text = "Enrolled";
        this.SetStatus(
            result.IsConfident
                ? $"Done — {result.Speakers.Count} recordings kept, agreement {result.Cohesion:F2}."
                : $"Kept {result.Speakers.Count} recordings, but they varied more than ideal (agreement {result.Cohesion:F2}).",
            result.IsConfident ? AcceptedColor : WarningColor,
            15);
        this.hintLabel.Text = result.IsConfident
            ? String.Empty
            : "Recognition will work, but re-enrolling somewhere quieter would make it more reliable.";
        this.Completed?.Invoke(this, result);
    }

    void Fail(string message)
    {
        this.recordProgress.IsVisible = false;
        this.StopMeter();
        this.currentPrompt = -1;
        this.RefreshPrompts();
        this.SetStatus("Enrollment stopped.", WarningColor, 15);
        this.hintLabel.Text = message;
        this.progressLabel.Text = String.Empty;
        this.Failed?.Invoke(this, message);
    }

    T? Resolve<T>() where T : class
        => this.Handler?.MauiContext?.Services?.GetService(typeof(T)) as T;
}
