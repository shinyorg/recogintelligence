using Shiny.FaceIntelligence;
using Shiny.FaceIntelligence.Onnx;
using Shiny.FaceIntelligence.DocumentDb.Sqlite;
using Shiny.DocumentIntelligence;
using Microsoft.Extensions.Logging;
using Shiny;

namespace Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseShinyShell(x => x.AddGeneratedMaps())   // registers page↔ViewModel maps + INavigator/IDialogs
            .UseShinyControls()   // Shiny.Maui.Controls (themes, feedback, etc.)
            .UseShinyCamera()     // registers the CameraView handler
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        var dataDir = FileSystem.AppDataDirectory;
        builder.Services.AddFaceIntelligence(face =>
        {
            face.Options.MaxDistance = 0.6f; // cosine distance; ~0.4 similarity for ArcFace same-person

            // The ArcFace model ships as a Resources/Raw asset (arcface.onnx). Load it from the app
            // package as bytes — bundled assets aren't real file paths on iOS/Android. The provider
            // runs lazily on first enroll/recognize, so a missing model surfaces there (handled by
            // the pages), not at startup.
            face.UseOnnxEmbedder(o => o.ModelBytesProvider = () => LoadBundledModel("arcface.onnx"));

            face.UseSqliteStore(o =>
            {
                o.ConnectionString = $"Data Source={Path.Combine(dataDir, "faces.db")}";
            });
        });

        // Native document scanner (IDocumentScanner): VisionKit on iOS, ML Kit on Android.
        builder.Services.AddDocumentIntelligence();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }

    /// <summary>
    /// Reads a bundled <c>Resources/Raw</c> asset into memory. Throws <see cref="FileNotFoundException"/>
    /// when the asset isn't present, which the pages surface as a "model missing" message.
    /// </summary>
    static byte[] LoadBundledModel(string assetFileName)
    {
        using var stream = FileSystem.OpenAppPackageFileAsync(assetFileName).GetAwaiter().GetResult();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
