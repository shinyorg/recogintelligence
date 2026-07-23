using Microsoft.Extensions.DependencyInjection;
using Shiny.VoiceIntelligence.DocumentDb.Sqlite;
using Shiny.VoiceIntelligence.Testing;
using Xunit;

namespace Shiny.VoiceIntelligence.Tests;

/// <summary>
/// End-to-end coverage of the enroll/recognize pipeline against the REAL Shiny.DocumentDb.Sqlite
/// vector store (sqlite-vec), using <see cref="FakeSpeakerEmbedder"/> so vector geometry is deterministic.
/// Exercises the actual <c>AddVoiceIntelligence</c> DI wiring, not hand-built objects. Mirrors the face suite.
/// </summary>
public class VoiceIntelligenceManagerTests : IDisposable
{
    static readonly string? Vec0 = Vec0Locator.Find();

    readonly List<ServiceProvider> providers = [];
    readonly string dbPath = Path.Combine(Path.GetTempPath(), $"voices_test_{Guid.NewGuid():N}.db");

    IVoiceIntelligence Create(int dim = 8, float maxDistance = 0.6f, int candidateCount = 10)
    {
        var services = new ServiceCollection();
        services.AddVoiceIntelligence(voice =>
        {
            voice.Options.MaxDistance = maxDistance;
            voice.Options.CandidateCount = candidateCount;
            voice.UseEmbedder(new FakeSpeakerEmbedder(dim)); // fake bypasses ONNX entirely
            voice.UseSqliteStore(o => o.ConnectionString = $"Data Source={this.dbPath}");
        });

        var sp = services.BuildServiceProvider();
        this.providers.Add(sp);
        return sp.GetRequiredService<IVoiceIntelligence>();
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
    public void Vec0Binary_IsAvailable_OnDeveloperMachine()
    {
        // Documents the requirement. When this fails the rest of the suite skips, and CI must
        // provision the sqlite-vec binary (download per-platform) next to the test assembly.
        Assert.True(Vec0 != null, "sqlite-vec native binary (vec0.dylib/.so/.dll) was not found next to the test assembly.");
    }

    [Fact]
    public async Task Enroll_Then_Recognize_SameSpeaker_Matches()
    {
        RequireVec0();
        var rec = this.Create();
        await rec.Enroll("Allan", TestVoices.Utterance(1, 0, 0, 0, 0, 0, 0, 0));

        var result = await rec.Recognize(TestVoices.Utterance(0.98f, 0.2f, 0, 0, 0, 0, 0, 0));

        Assert.True(result.IsMatch);
        Assert.Equal("Allan", result.PersonIdentifier);
        Assert.NotNull(result.DocumentId);
        Assert.True(result.Distance < 0.1f, $"expected a tiny distance, got {result.Distance}");
    }

    [Fact]
    public async Task Recognize_DifferentSpeaker_NoMatch()
    {
        RequireVec0();
        var rec = this.Create();
        await rec.Enroll("Allan", TestVoices.Utterance(1, 0, 0, 0, 0, 0, 0, 0));

        // Orthogonal vector → cosine distance ~1.0, well beyond the 0.6 threshold.
        var result = await rec.Recognize(TestVoices.Utterance(0, 1, 0, 0, 0, 0, 0, 0));

        Assert.False(result.IsMatch);
        Assert.Null(result.PersonIdentifier);
    }

    [Fact]
    public async Task Recognize_EmptyStore_NoMatch()
    {
        RequireVec0();
        var rec = this.Create();

        var result = await rec.Recognize(TestVoices.Utterance(1, 0, 0, 0, 0, 0, 0, 0));

        Assert.False(result.IsMatch);
    }

    [Fact]
    public async Task CosineDistance_MatchesExpectedGeometry()
    {
        RequireVec0();
        var rec = this.Create();
        await rec.Enroll("Allan", TestVoices.Utterance(1, 0, 0, 0, 0, 0, 0, 0));

        // Query at 60° to the enrolled vector → cosine similarity 0.5 → cosine distance 0.5.
        var result = await rec.Recognize(TestVoices.Utterance(0.5f, 0.8660254f, 0, 0, 0, 0, 0, 0));

        Assert.True(result.IsMatch); // 0.5 < 0.6 threshold
        Assert.Equal(0.5f, result.Distance, 0.02f);
        Assert.Equal(0.5f, result.Similarity, 0.02f);
    }

    [Fact]
    public async Task MultipleUtterances_MatchNearestAcrossGallery()
    {
        RequireVec0();
        var rec = this.Create();
        // Two very different "utterances" of the same speaker.
        await rec.Enroll("Allan", TestVoices.Utterance(1, 0, 0, 0, 0, 0, 0, 0));
        await rec.Enroll("Allan", TestVoices.Utterance(0, 1, 0, 0, 0, 0, 0, 0));

        // Query close to the second utterance — recognition takes the nearest neighbor across both.
        var result = await rec.Recognize(TestVoices.Utterance(0.15f, 0.99f, 0, 0, 0, 0, 0, 0));

        Assert.True(result.IsMatch);
        Assert.Equal("Allan", result.PersonIdentifier);
        Assert.True(result.Distance < 0.05f, $"expected nearest-utterance distance, got {result.Distance}");
    }

    [Fact]
    public async Task MaxDistance_Threshold_IsEnforced()
    {
        RequireVec0();
        var rec = this.Create(maxDistance: 0.4f);
        await rec.Enroll("Allan", TestVoices.Utterance(1, 0, 0, 0, 0, 0, 0, 0));

        // Distance 0.5 > 0.4 → rejected.
        Assert.False((await rec.Recognize(TestVoices.Utterance(0.5f, 0.8660254f, 0, 0, 0, 0, 0, 0))).IsMatch);
        // Distance ~0.02 < 0.4 → accepted.
        Assert.True((await rec.Recognize(TestVoices.Utterance(0.98f, 0.2f, 0, 0, 0, 0, 0, 0))).IsMatch);
    }

    [Fact]
    public async Task Forget_RemovesEveryUtteranceForName()
    {
        RequireVec0();
        var rec = this.Create();
        await rec.Enroll("Allan", TestVoices.Utterance(1, 0, 0, 0, 0, 0, 0, 0));
        await rec.Enroll("Allan", TestVoices.Utterance(0, 1, 0, 0, 0, 0, 0, 0));

        var removed = await rec.Forget("Allan");

        Assert.Equal(2, removed);
        Assert.Empty(await rec.GetAll());
        // And the vectors are gone from the ANN sidecar too.
        Assert.False((await rec.Recognize(TestVoices.Utterance(1, 0, 0, 0, 0, 0, 0, 0))).IsMatch);
    }

    [Fact]
    public async Task GetAll_ReturnsOnePerUtterance()
    {
        RequireVec0();
        var rec = this.Create();
        await rec.Enroll("Allan", TestVoices.Utterance(1, 0, 0, 0, 0, 0, 0, 0));
        await rec.Enroll("Allan", TestVoices.Utterance(0, 1, 0, 0, 0, 0, 0, 0));
        await rec.Enroll("Bob", TestVoices.Utterance(0, 0, 1, 0, 0, 0, 0, 0));

        var all = await rec.GetAll();

        Assert.Equal(3, all.Count);
        Assert.Equal(2, all.Count(p => p.PersonIdentifier == "Allan"));
        Assert.Contains(all, p => p.PersonIdentifier == "Bob");
    }

    [Fact]
    public async Task VectorDimension_IsReadFromEmbedder()
    {
        // AddVoiceIntelligence maps the vector with embedder.Dimensions; a non-default width must
        // round-trip end to end (mapping, insert, ANN search) without dimension errors.
        RequireVec0();
        var rec = this.Create(dim: 192);
        var v = new float[192];
        v[3] = 1f;
        await rec.Enroll("Allan", TestVoices.Utterance(v));

        var query = new float[192];
        query[3] = 0.97f;
        query[4] = 0.24f;
        var result = await rec.Recognize(TestVoices.Utterance(query));

        Assert.True(result.IsMatch);
        Assert.Equal("Allan", result.PersonIdentifier);
    }
}
