using Microsoft.Extensions.DependencyInjection;
using Shiny.FaceIntelligence.DocumentDb.Sqlite;
using Shiny.FaceIntelligence.Testing;
using Xunit;

namespace Shiny.FaceIntelligence.Tests;

/// <summary>
/// End-to-end coverage of the enroll/recognize pipeline against the REAL Shiny.DocumentDb.Sqlite
/// vector store (sqlite-vec), using <see cref="FakeEmbedder"/> so vector geometry is deterministic.
/// Exercises the actual <c>AddFaceIntelligence</c> DI wiring, not hand-built objects.
/// </summary>
public class FaceIntelligenceManagerTests : IDisposable
{
    static readonly string? Vec0 = Vec0Locator.Find();
    static readonly FaceBox AnyBox = new(0, 0, 100, 100);

    readonly List<ServiceProvider> providers = [];
    readonly string dbPath = Path.Combine(Path.GetTempPath(), $"faces_test_{Guid.NewGuid():N}.db");

    IFaceIntelligence Create(int dim = 8, float maxDistance = 0.6f, int candidateCount = 10)
    {
        var services = new ServiceCollection();
        services.AddFaceIntelligence(face =>
        {
            face.Options.MaxDistance = maxDistance;
            face.Options.CandidateCount = candidateCount;
            face.UseEmbedder(new FakeEmbedder(dim)); // fake bypasses ONNX entirely
            face.UseSqliteStore(o =>
            {
                o.ConnectionString = $"Data Source={this.dbPath}";
                // vec0 is registered as a SQLite auto-extension by UseSqliteStore (preloaded model);
                // the native binary is resolved from the package's runtimes/<rid>/native assets. The
                // Vec0Locator guard (RequireVec0) skips when that binary isn't present.
            });
        });

        var sp = services.BuildServiceProvider();
        this.providers.Add(sp);
        return sp.GetRequiredService<IFaceIntelligence>();
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
    public async Task Enroll_Then_Recognize_SamePerson_Matches()
    {
        RequireVec0();
        var rec = this.Create();
        await rec.Enroll("Allan", TestFaces.Image(1, 0, 0, 0, 0, 0, 0, 0), AnyBox);

        var result = await rec.Recognize(TestFaces.Image(0.98f, 0.2f, 0, 0, 0, 0, 0, 0), AnyBox);

        Assert.True(result.IsMatch);
        Assert.Equal("Allan", result.Name);
        Assert.NotNull(result.PersonId);
        Assert.True(result.Distance < 0.1f, $"expected a tiny distance, got {result.Distance}");
    }

    [Fact]
    public async Task Recognize_DifferentPerson_NoMatch()
    {
        RequireVec0();
        var rec = this.Create();
        await rec.Enroll("Allan", TestFaces.Image(1, 0, 0, 0, 0, 0, 0, 0), AnyBox);

        // Orthogonal vector → cosine distance ~1.0, well beyond the 0.6 threshold.
        var result = await rec.Recognize(TestFaces.Image(0, 1, 0, 0, 0, 0, 0, 0), AnyBox);

        Assert.False(result.IsMatch);
        Assert.Null(result.Name);
    }

    [Fact]
    public async Task Recognize_EmptyStore_NoMatch()
    {
        RequireVec0();
        var rec = this.Create();

        var result = await rec.Recognize(TestFaces.Image(1, 0, 0, 0, 0, 0, 0, 0), AnyBox);

        Assert.False(result.IsMatch);
    }

    [Fact]
    public async Task CosineDistance_MatchesExpectedGeometry()
    {
        RequireVec0();
        var rec = this.Create();
        await rec.Enroll("Allan", TestFaces.Image(1, 0, 0, 0, 0, 0, 0, 0), AnyBox);

        // Query at 60° to the enrolled vector → cosine similarity 0.5 → cosine distance 0.5.
        var result = await rec.Recognize(TestFaces.Image(0.5f, 0.8660254f, 0, 0, 0, 0, 0, 0), AnyBox);

        Assert.True(result.IsMatch); // 0.5 < 0.6 threshold
        Assert.Equal(0.5f, result.Distance, 0.02f);
        Assert.Equal(0.5f, result.Similarity, 0.02f);
    }

    [Fact]
    public async Task MultipleShots_MatchNearestAcrossGallery()
    {
        RequireVec0();
        var rec = this.Create();
        // Two very different "poses" of the same person.
        await rec.Enroll("Allan", TestFaces.Image(1, 0, 0, 0, 0, 0, 0, 0), AnyBox);
        await rec.Enroll("Allan", TestFaces.Image(0, 1, 0, 0, 0, 0, 0, 0), AnyBox);

        // Query close to the second shot — recognition takes the nearest neighbor across both.
        var result = await rec.Recognize(TestFaces.Image(0.15f, 0.99f, 0, 0, 0, 0, 0, 0), AnyBox);

        Assert.True(result.IsMatch);
        Assert.Equal("Allan", result.Name);
        Assert.True(result.Distance < 0.05f, $"expected nearest-shot distance, got {result.Distance}");
    }

    [Fact]
    public async Task AddingCloserShot_ReducesMatchDistance()
    {
        RequireVec0();
        var rec = this.Create();
        var query = TestFaces.Image(0.5f, 0.8660254f, 0, 0, 0, 0, 0, 0);

        await rec.Enroll("Allan", TestFaces.Image(1, 0, 0, 0, 0, 0, 0, 0), AnyBox);
        var before = (await rec.Recognize(query, AnyBox)).Distance; // ~0.5

        // A second shot pointing the same way as the query.
        await rec.Enroll("Allan", TestFaces.Image(0.5f, 0.8660254f, 0, 0, 0, 0, 0, 0), AnyBox);
        var after = (await rec.Recognize(query, AnyBox)).Distance;  // ~0

        Assert.True(after < before, $"adding a closer shot should reduce distance: before={before}, after={after}");
        Assert.True(after < 0.01f);
    }

    [Fact]
    public async Task MaxDistance_Threshold_IsEnforced()
    {
        RequireVec0();
        var rec = this.Create(maxDistance: 0.4f);
        await rec.Enroll("Allan", TestFaces.Image(1, 0, 0, 0, 0, 0, 0, 0), AnyBox);

        // Distance 0.5 > 0.4 → rejected.
        Assert.False((await rec.Recognize(TestFaces.Image(0.5f, 0.8660254f, 0, 0, 0, 0, 0, 0), AnyBox)).IsMatch);
        // Distance ~0.02 < 0.4 → accepted.
        Assert.True((await rec.Recognize(TestFaces.Image(0.98f, 0.2f, 0, 0, 0, 0, 0, 0), AnyBox)).IsMatch);
    }

    [Fact]
    public async Task SameName_DifferentPeople_AreConflated()
    {
        // Documents the current name-as-identity behavior (see CLAUDE.md TODO: decouple identity
        // from display name). Two orthogonal faces enrolled under one name both report that name.
        RequireVec0();
        var rec = this.Create();
        var a = await rec.Enroll("Twin", TestFaces.Image(1, 0, 0, 0, 0, 0, 0, 0), AnyBox);
        var b = await rec.Enroll("Twin", TestFaces.Image(0, 1, 0, 0, 0, 0, 0, 0), AnyBox);

        var r0 = await rec.Recognize(TestFaces.Image(0.98f, 0.2f, 0, 0, 0, 0, 0, 0), AnyBox);
        var r1 = await rec.Recognize(TestFaces.Image(0.2f, 0.98f, 0, 0, 0, 0, 0, 0), AnyBox);

        Assert.Equal("Twin", r0.Name);
        Assert.Equal("Twin", r1.Name);
        Assert.Equal(a.Id, r0.PersonId);
        Assert.Equal(b.Id, r1.PersonId);
        Assert.NotEqual(r0.PersonId, r1.PersonId); // distinct documents, same label
    }

    [Fact]
    public async Task GetAll_ReturnsOnePerShot()
    {
        RequireVec0();
        var rec = this.Create();
        await rec.Enroll("Allan", TestFaces.Image(1, 0, 0, 0, 0, 0, 0, 0), AnyBox);
        await rec.Enroll("Allan", TestFaces.Image(0, 1, 0, 0, 0, 0, 0, 0), AnyBox);
        await rec.Enroll("Bob", TestFaces.Image(0, 0, 1, 0, 0, 0, 0, 0), AnyBox);

        var all = await rec.GetAll();

        Assert.Equal(3, all.Count);
        Assert.Equal(2, all.Count(p => p.Name == "Allan"));
        Assert.Contains(all, p => p.Name == "Bob");
    }

    [Fact]
    public async Task Forget_RemovesEveryShotForName()
    {
        RequireVec0();
        var rec = this.Create();
        await rec.Enroll("Allan", TestFaces.Image(1, 0, 0, 0, 0, 0, 0, 0), AnyBox);
        await rec.Enroll("Allan", TestFaces.Image(0, 1, 0, 0, 0, 0, 0, 0), AnyBox);

        var removed = await rec.Forget("Allan");

        Assert.Equal(2, removed);
        Assert.Empty(await rec.GetAll());
        // And the vectors are gone from the ANN sidecar too.
        Assert.False((await rec.Recognize(TestFaces.Image(1, 0, 0, 0, 0, 0, 0, 0), AnyBox)).IsMatch);
    }

    [Fact]
    public async Task VectorDimension_IsReadFromEmbedder()
    {
        // AddFaceIntelligence maps the vector with embedder.Dimensions; a non-512 embedder must
        // round-trip end to end (mapping, insert, ANN search) without dimension errors.
        RequireVec0();
        var rec = this.Create(dim: 16);
        var v = new float[16];
        v[3] = 1f;
        await rec.Enroll("Allan", TestFaces.Image(v), AnyBox);

        var query = new float[16];
        query[3] = 0.97f;
        query[4] = 0.24f;
        var result = await rec.Recognize(TestFaces.Image(query), AnyBox);

        Assert.True(result.IsMatch);
        Assert.Equal("Allan", result.Name);
    }
}
