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

            face.UseSqliteStore(o =>
            {
                o.ConnectionString = $"Data Source={Path.Combine(dataDir, "faces.db")}";
            });
        });
    }

    public void Use(IPlatformApplication app) { }
}
