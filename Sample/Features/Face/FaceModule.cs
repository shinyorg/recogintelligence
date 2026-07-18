using Microsoft.Extensions.DependencyInjection;
using Sample.Infrastructure;
using Shiny;
using Shiny.FaceIntelligence;
using Shiny.FaceIntelligence.Onnx;
using Shiny.FaceIntelligence.DocumentDb.Sqlite;

namespace Sample.Features.Face;

/// <summary>Registers the face intelligence pipeline: ONNX ArcFace embedder + sqlite-vec store.</summary>
public class FaceModule : IMauiModule
{
    public void Add(MauiAppBuilder builder)
    {
        var dataDir = FileSystem.AppDataDirectory;
        builder.Services.AddFaceIntelligence(face =>
        {
            face.Options.MaxDistance = 0.6f; // cosine distance; ~0.4 similarity for ArcFace same-person

            // The ArcFace model ships as a Resources/Raw asset (arcface.onnx), loaded lazily on first
            // enroll/recognize. A missing model surfaces there (handled by the pages), not at startup.
            face.UseOnnxEmbedder(o => o.ModelBytesProvider = () => BundledAssets.LoadBundledModel("arcface.onnx"));

            // UltraFace detector (face_detector.onnx, Resources/Raw) drives the no-box enrollment: ONNX finds
            // the face on a single captured still and the manager rejects no/low-confidence/multiple/too-small
            // faces — no camera frame analyzer needed. Also loaded lazily on first enroll.
            face.UseOnnxDetector(o => o.ModelBytesProvider = () => BundledAssets.LoadBundledModel("face_detector.onnx"));

            face.UseSqliteStore(o =>
            {
                o.ConnectionString = $"Data Source={Path.Combine(dataDir, "faces.db")}";
            });
        });
    }

    public void Use(IPlatformApplication app) { }
}
