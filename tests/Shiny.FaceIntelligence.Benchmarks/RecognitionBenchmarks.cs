using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Shiny.FaceIntelligence;
using Shiny.FaceIntelligence.DocumentDb.Sqlite;
using Shiny.FaceIntelligence.Testing;

namespace Shiny.FaceIntelligence.Benchmarks;

/// <summary>
/// Measures recognition latency (embed + sqlite-vec NearestVectors + threshold) as the enrolled
/// gallery grows. Uses <see cref="FakeEmbedder"/> at the real 512-d ArcFace width so the store path
/// is representative without needing an ONNX model. Enroll is part of the (unmeasured) setup.
/// </summary>
[MemoryDiagnoser]
public class RecognitionBenchmarks
{
    const int Dim = 512;
    static readonly FaceBox Box = new(0, 0, 100, 100);

    [Params(100, 1_000, 10_000)]
    public int GallerySize;

    ServiceProvider sp = null!;
    IFaceIntelligence rec = null!;
    byte[] query = null!;
    string dbPath = null!;

    [GlobalSetup]
    public void Setup()
    {
        var vec0 = Vec0Locator.Find()
            ?? throw new InvalidOperationException("sqlite-vec native binary not found for benchmark.");

        this.dbPath = Path.Combine(Path.GetTempPath(), $"faces_bench_{this.GallerySize}_{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddFaceIntelligence(face =>
        {
            face.Options.MaxDistance = 2f; // accept everything so Recognize always does the full lookup
            face.Options.CandidateCount = 10;
            face.UseEmbedder(new FakeEmbedder(Dim));
            face.UseSqliteStore(o =>
            {
                o.ConnectionString = $"Data Source={this.dbPath}";
                o.VectorExtensionPath = vec0;
            });
        });

        this.sp = services.BuildServiceProvider();
        this.rec = this.sp.GetRequiredService<IFaceIntelligence>();

        var rng = new Random(42);
        for (var i = 0; i < this.GallerySize; i++)
            this.rec.Enroll($"person-{i}", TestFaces.Image(RandomUnitVector(rng)), Box).GetAwaiter().GetResult();

        this.query = TestFaces.Image(RandomUnitVector(rng));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.sp.Dispose();
        var dir = Path.GetDirectoryName(this.dbPath)!;
        foreach (var f in Directory.GetFiles(dir, Path.GetFileName(this.dbPath) + "*"))
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }
    }

    [Benchmark]
    public Task<RecognitionResult> Recognize() => this.rec.Recognize(this.query, Box);

    static float[] RandomUnitVector(Random rng)
    {
        var v = new float[Dim];
        for (var i = 0; i < Dim; i++)
            v[i] = (float)((rng.NextDouble() * 2) - 1);
        return v;
    }
}
