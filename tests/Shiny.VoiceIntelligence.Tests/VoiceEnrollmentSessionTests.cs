using Microsoft.Extensions.DependencyInjection;
using Shiny.VoiceIntelligence.DocumentDb.Sqlite;
using Shiny.VoiceIntelligence.Testing;
using Xunit;

namespace Shiny.VoiceIntelligence.Tests;

/// <summary>
/// The guided enrollment wizard (<see cref="VoiceEnrollmentSession"/>) against the REAL sqlite-vec store
/// with <see cref="FakeSpeakerEmbedder"/>, so "recordings" are vectors and every agreement decision is
/// exact arithmetic rather than a property of some audio file.
/// </summary>
/// <remarks>
/// The audio gates are switched off in most tests: the fake embedder reads the sample buffer AS the
/// voiceprint, so those buffers are unit vectors, not sound — they'd be rejected as clipped silence.
/// <see cref="Submit_SilentRecording_IsRejected"/> covers the gates with a buffer that really is audio.
/// </remarks>
public class VoiceEnrollmentSessionTests : IDisposable
{
    static readonly string? Vec0 = Vec0Locator.Find();

    readonly List<ServiceProvider> providers = [];
    readonly string dbPath = Path.Combine(Path.GetTempPath(), $"voices_wizard_{Guid.NewGuid():N}.db");

    IVoiceIntelligence Create(float maxDistance = 0.4f)
    {
        var services = new ServiceCollection();
        services.AddVoiceIntelligence(voice =>
        {
            voice.Options.MaxDistance = maxDistance;
            voice.UseEmbedder(new FakeSpeakerEmbedder(8));
            voice.UseSqliteStore(o => o.ConnectionString = $"Data Source={this.dbPath}");
        });

        var sp = services.BuildServiceProvider();
        this.providers.Add(sp);
        return sp.GetRequiredService<IVoiceIntelligence>();
    }

    /// <summary>Enrollment options with the audio gates off — see the note on this class.</summary>
    static VoiceEnrollmentOptions Silent(float maxDistance = 0.4f)
    {
        var o = VoiceEnrollmentOptions.ForThreshold(maxDistance);
        o.MinSpeechSeconds = 0;
        o.MinSpeechLevel = 0;
        o.MaxClippedFraction = 0;
        o.MinSnrDb = -1;
        return o;
    }

    /// <summary>A "recording" whose voiceprint is a unit vector <paramref name="degrees"/> off e1.</summary>
    static float[] Voice(double degrees, int plane = 1)
    {
        var v = new float[8];
        var rad = degrees * Math.PI / 180;
        v[0] = (float)Math.Cos(rad);
        v[plane] = (float)Math.Sin(rad);
        return v;
    }

    static void RequireVec0()
    {
        if (Vec0 == null)
            Assert.Skip("sqlite-vec native binary (vec0.dylib/.so/.dll) not present next to the test assembly.");
    }

    public void Dispose()
    {
        foreach (var p in this.providers)
            p.Dispose();

        var dir = Path.GetDirectoryName(this.dbPath)!;
        var prefix = Path.GetFileName(this.dbPath);
        foreach (var f in Directory.GetFiles(dir, prefix + "*"))
        {
            try { File.Delete(f); } catch { /* best effort: -wal/-shm sidecars */ }
        }
    }

    [Fact]
    public async Task Completes_Once_Enough_Consistent_Recordings()
    {
        RequireVec0();
        var voice = this.Create();
        var session = voice.CreateEnrollment("Allan", Silent());

        VoiceEnrollmentStepResult? last = null;
        for (var i = 0; i < 3; i++)
        {
            Assert.False(session.IsComplete);
            last = await session.Submit(Voice(i * 2));   // 0°, 2°, 4° apart — the same person
            Assert.True(last.Accepted);
        }

        Assert.True(session.IsComplete);
        Assert.NotNull(last!.Result);
        Assert.Same(session.Result, last.Result);
        Assert.True(last.Result!.IsConfident);
        Assert.Equal(3, last.Result.Speakers.Count);
        Assert.True(last.Result.Cohesion < 0.01f, $"expected tight cohesion, got {last.Result.Cohesion}");

        var stored = await voice.GetAll();
        Assert.Equal(3, stored.Count);
        Assert.All(stored, s => Assert.Equal("Allan", s.PersonIdentifier));
    }

    [Fact]
    public async Task Nothing_Is_Stored_Until_The_Session_Completes()
    {
        RequireVec0();
        var voice = this.Create();
        var session = voice.CreateEnrollment("Allan", Silent());

        await session.Submit(Voice(0));
        await session.Submit(Voice(2));

        // Two of the three needed: an abandoned wizard must not leave a half-enrolled speaker behind.
        Assert.False(session.IsComplete);
        Assert.Empty(await voice.GetAll());
        Assert.Equal(1, session.SamplesStillNeeded);
    }

    [Fact]
    public async Task Recording_That_Disagrees_With_The_Others_Is_Rejected()
    {
        RequireVec0();
        var voice = this.Create();
        var session = voice.CreateEnrollment("Allan", Silent());

        await session.Submit(Voice(0));
        await session.Submit(Voice(2));

        var step = await session.Submit(Voice(90));   // orthogonal: distance 1.0, way past MaxOutlierDistance

        Assert.False(step.Accepted);
        Assert.Equal(VoiceEnrollmentRejection.Inconsistent, step.Reason);
        Assert.NotEmpty(step.Hint);
        Assert.Equal(2, session.AcceptedCount);
        Assert.False(session.IsComplete);
    }

    [Fact]
    public async Task Two_Agreeing_Recordings_Outvote_A_Bad_First_One()
    {
        RequireVec0();
        var voice = this.Create();
        var session = voice.CreateEnrollment("Allan", Silent());

        // The first recording is the broken one — but nothing can know that yet.
        await session.Submit(Voice(90));

        // Two clips that disagree with it but agree with each other. The first is rejected (ambiguous),
        // the second flips the verdict: the lone survivor was the outlier.
        var rejected = await session.Submit(Voice(0));
        Assert.False(rejected.Accepted);
        Assert.Equal(VoiceEnrollmentRejection.Inconsistent, rejected.Reason);

        var rescued = await session.Submit(Voice(2));
        Assert.True(rescued.Accepted);
        Assert.Equal(2, session.AcceptedCount);       // the two good ones, not the bad first one

        var final = await session.Submit(Voice(4));
        Assert.True(final.Accepted);
        Assert.True(session.IsComplete);
        Assert.True(session.Result!.Cohesion < 0.01f, "the rescued set should be the tight one");
    }

    [Fact]
    public async Task Gives_Up_At_MaxSamples_And_Stores_Its_Best_Subset()
    {
        RequireVec0();
        var voice = this.Create();

        // Cohesion can never be met: every pair sits ~0.1 apart, the target is 0.05, and none of them is
        // far enough out to be rejected outright.
        var options = Silent();
        options.MinSamples = 2;
        options.MaxSamples = 3;
        options.MaxCohesionDistance = 0.05f;
        options.MaxOutlierDistance = 0.5f;

        var session = voice.CreateEnrollment("Allan", options);
        await session.Submit(Voice(0));
        await session.Submit(Voice(26));              // ~0.10 from the first
        var last = await session.Submit(Voice(-26, plane: 2));

        Assert.True(session.IsComplete);
        Assert.False(last.Result!.IsConfident);       // stored, but flagged as not-great
        Assert.Equal(2, last.Result.Speakers.Count);  // pruned from 3 down to MinSamples
        Assert.Equal(2, (await voice.GetAll()).Count);
    }

    [Fact]
    public async Task Submit_SilentRecording_IsRejected()
    {
        RequireVec0();
        var voice = this.Create();

        // Real audio this time (5 s of near-silence at 16 kHz), so the audio gates are left ON.
        var session = voice.CreateEnrollment("Allan");
        var step = await session.Submit(new float[16000 * 5]);

        Assert.False(step.Accepted);
        Assert.Equal(VoiceEnrollmentRejection.TooLittleSpeech, step.Reason);
        Assert.Equal(0, session.AcceptedCount);
        Assert.Equal(5f, step.Metrics.Seconds, 2);
    }

    [Fact]
    public async Task RejectedRecording_StaysOnTheSameSentence()
    {
        RequireVec0();
        var voice = this.Create();
        var session = voice.CreateEnrollment("Allan", Silent());

        await session.Submit(Voice(0));
        var prompt = session.CurrentPrompt;
        var index = session.CurrentPromptIndex;

        // Far enough out to be rejected: the person gets the same line again rather than being moved along,
        // which is what makes "keep going until it passes" legible instead of looking like a cycle.
        var step = await session.Submit(Voice(80));

        Assert.False(step.Accepted);
        Assert.Equal(prompt, session.CurrentPrompt);
        Assert.Equal(index, session.CurrentPromptIndex);
        Assert.Equal(2, session.AttemptCount);   // the attempt still counted
    }

    [Fact]
    public async Task Prompts_Advance_OnAcceptance_AndSubmittingAfterCompletionThrows()
    {
        RequireVec0();
        var voice = this.Create();
        var session = voice.CreateEnrollment("Allan", Silent());

        var first = session.CurrentPrompt;
        await session.Submit(Voice(0));
        Assert.NotEqual(first, session.CurrentPrompt);

        await session.Submit(Voice(2));
        await session.Submit(Voice(4));
        Assert.True(session.IsComplete);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Submit(Voice(6)));

        session.Reset();
        Assert.False(session.IsComplete);
        Assert.Equal(0, session.AcceptedCount);
    }

    [Fact]
    public async Task Finish_StoresTheBestSubset_WhenTheCallerStopsAsking()
    {
        RequireVec0();
        var voice = this.Create();

        // A caller with its own ceiling (VoiceEnrollmentView.MaxAttempts) gives up before the session's
        // own MaxSamples is reached. Without Finish(), everything recorded so far is thrown away.
        var options = Silent();
        options.MinSamples = 2;
        options.MaxSamples = 10;
        options.MaxCohesionDistance = 0.05f;
        options.MaxOutlierDistance = 0.5f;

        var session = voice.CreateEnrollment("Allan", options);
        await session.Submit(Voice(0));
        await session.Submit(Voice(26));            // ~0.10 apart — never coherent enough to self-complete
        Assert.False(session.IsComplete);

        var result = await session.Finish();

        Assert.NotNull(result);
        Assert.True(session.IsComplete);
        Assert.False(result!.IsConfident);          // kept, but flagged
        Assert.Equal(2, result.Speakers.Count);
        Assert.Equal(2, (await voice.GetAll()).Count);
    }

    [Fact]
    public async Task Finish_StoresNothing_WhenTooFewRecordingsWereAccepted()
    {
        RequireVec0();
        var voice = this.Create();

        var options = Silent();
        options.MinSamples = 3;

        var session = voice.CreateEnrollment("Allan", options);
        await session.Submit(Voice(0));

        // One clip is not an enrollment — storing it would create exactly the weak single-template set the
        // whole session exists to prevent, so the caller gets null and nothing is written.
        var result = await session.Finish();

        Assert.Null(result);
        Assert.False(session.IsComplete);
        Assert.Empty(await voice.GetAll());
    }

    [Fact]
    public async Task Finish_OnACompletedSession_IsANoOp()
    {
        RequireVec0();
        var voice = this.Create();

        var options = Silent();
        options.MinSamples = 2;

        var session = voice.CreateEnrollment("Allan", options);
        await session.Submit(Voice(0));
        await session.Submit(Voice(2));
        Assert.True(session.IsComplete);

        var again = await session.Finish();

        Assert.Same(session.Result, again);
        Assert.Equal(2, (await voice.GetAll()).Count);   // not stored twice
    }

    [Fact]
    public void Defaults_Are_Derived_From_The_Match_Threshold()
    {
        // A template set whose own members sit as far apart as the match threshold leaves a probe no
        // headroom at all, so the cohesion target has to be tighter than the threshold.
        var options = VoiceEnrollmentOptions.ForThreshold(0.40f);

        Assert.Equal(0.30f, options.MaxCohesionDistance, 3);
        Assert.Equal(0.40f, options.MaxOutlierDistance, 3);
        Assert.True(options.MaxCohesionDistance < options.MaxOutlierDistance);
    }
}
