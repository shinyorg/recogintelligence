using Microsoft.Extensions.DependencyInjection;
using Shiny.DocumentDb;
using Shiny.FaceIntelligence;
using Shiny.FaceIntelligence.DocumentDb.Sqlite;
using Shiny.FaceIntelligence.Testing;
using Shiny.VoiceIntelligence;
using Shiny.VoiceIntelligence.DocumentDb.Sqlite;
using Xunit;

namespace Shiny.RecognitionIntelligence.IntegrationTests;

/// <summary>
/// Proves the face and voice pipelines COMPOSE in a single container. Both DocumentDb stacks used to
/// register a shared <c>IDocumentStore</c> singleton, so with both present the last registration won —
/// the face store silently resolved the voice store (Speaker-mapped, voices.db) and vice versa. These
/// tests register both (voice AFTER face — the order that triggered the bug) and assert isolation.
/// The fake embedders use DIFFERENT dimensions (face 8, voice 6) so any cross-wiring fails loudly on a
/// vector-dimension mismatch rather than silently corrupting data.
/// </summary>
public class CombinedStacksTests : IDisposable
{
    const int FaceDim = 8;
    const int VoiceDim = 6;

    readonly List<ServiceProvider> providers = [];
    readonly string faceDb = Path.Combine(Path.GetTempPath(), $"combo_faces_{Guid.NewGuid():N}.db");
    readonly string voiceDb = Path.Combine(Path.GetTempPath(), $"combo_voices_{Guid.NewGuid():N}.db");

    ServiceProvider BuildBothStacks()
    {
        var services = new ServiceCollection();

        // Face registered FIRST.
        services.AddFaceIntelligence(face =>
        {
            face.UseEmbedder(new FakeEmbedder(FaceDim));
            face.UseSqliteStore(o => o.ConnectionString = $"Data Source={this.faceDb}");
        });

        // Voice registered AFTER face — the ordering under which the old shared-IDocumentStore bug bit.
        services.AddVoiceIntelligence(voice =>
        {
            voice.UseEmbedder(new Shiny.VoiceIntelligence.Testing.FakeSpeakerEmbedder(VoiceDim));
            voice.UseSqliteStore(o => o.ConnectionString = $"Data Source={this.voiceDb}");
        });

        var sp = services.BuildServiceProvider();
        this.providers.Add(sp);
        return sp;
    }

    static void RequireVec0()
    {
        if (Vec0Locator.Find() == null)
            Assert.Skip("sqlite-vec native binary (vec0.dylib/.so/.dll) not present next to the test assembly.");
    }

    [Fact]
    public void NeitherStack_Leaks_A_Shared_IDocumentStore_Into_The_Container()
    {
        var services = new ServiceCollection();
        services.AddFaceIntelligence(face =>
        {
            face.UseEmbedder(new FakeEmbedder(FaceDim));
            face.UseSqliteStore(o => o.ConnectionString = $"Data Source={this.faceDb}");
        });
        services.AddVoiceIntelligence(voice =>
        {
            voice.UseEmbedder(new Shiny.VoiceIntelligence.Testing.FakeSpeakerEmbedder(VoiceDim));
            voice.UseSqliteStore(o => o.ConnectionString = $"Data Source={this.voiceDb}");
        });

        // Each stack owns a PRIVATE DocumentStore; nothing generic leaks into the app container where it
        // could collide (with the other stack or with the consumer's own DocumentDb usage).
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IDocumentStore));
    }

    [Fact]
    public async Task BothStacks_Enroll_And_Recognize_Without_Crossing_Stores()
    {
        RequireVec0();
        var sp = this.BuildBothStacks();
        var face = sp.GetRequiredService<IFaceIntelligence>();
        var voice = sp.GetRequiredService<IVoiceIntelligence>();

        // 8-d face vector and 6-d voice vector. Under the old bug one of these Enrolls would hit the
        // other store's mapping (wrong dimension) and throw; with per-stack stores both succeed.
        await face.Enroll("FacePerson", TestFaces.Image(1, 0, 0, 0, 0, 0, 0, 0), new FaceBox(0, 0, 100, 100));
        await voice.Enroll("VoiceSpeaker", Shiny.VoiceIntelligence.Testing.TestVoices.Utterance(1, 0, 0, 0, 0, 0));

        var people = await face.GetAll();
        var speakers = await voice.GetAll();

        // Each store holds only its own document type + name — no cross-contamination.
        Assert.Equal(["FacePerson"], people.Select(p => p.PersonIdentifier));
        Assert.Equal(["VoiceSpeaker"], speakers.Select(s => s.PersonIdentifier));

        // Each recognizes its own enrolled vector.
        var faceMatch = await face.Recognize(TestFaces.Image(1, 0, 0, 0, 0, 0, 0, 0), new FaceBox(0, 0, 100, 100));
        Assert.True(faceMatch.IsMatch);
        Assert.Equal("FacePerson", faceMatch.PersonIdentifier);

        var voiceMatch = await voice.Recognize(Shiny.VoiceIntelligence.Testing.TestVoices.Utterance(1, 0, 0, 0, 0, 0));
        Assert.True(voiceMatch.IsMatch);
        Assert.Equal("VoiceSpeaker", voiceMatch.PersonIdentifier);

        // The two stacks wrote to their own database files.
        Assert.True(File.Exists(this.faceDb));
        Assert.True(File.Exists(this.voiceDb));
    }

    public void Dispose()
    {
        foreach (var p in this.providers)
            p.Dispose();

        foreach (var db in new[] { this.faceDb, this.voiceDb })
        {
            var dir = Path.GetDirectoryName(db)!;
            var prefix = Path.GetFileName(db);
            foreach (var f in Directory.GetFiles(dir, prefix + "*"))
            {
                try { File.Delete(f); } catch { /* best effort: -wal/-shm sidecars */ }
            }
        }
    }
}
