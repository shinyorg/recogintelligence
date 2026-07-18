using Microsoft.Extensions.Logging;
using Sample.Features.Documents;
using Sample.Features.Face;
using Sample.Features.Voice;
using Shiny;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent;
#endif

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
            .AddInfrastructureModules(  // per-feature registration (vertical slices)
                new FaceModule(),
                new VoiceModule(),
                new DocumentsModule()
            )
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
        builder.AddMauiDevFlowAgent();
#endif
        return builder.Build();
    }
}
