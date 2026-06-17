using UIKit;

namespace Shiny.DocumentIntelligence;

static class ApplePlatform
{
    /// <summary>The top-most presented view controller of the active foreground window — what to present from.</summary>
    public static UIViewController? GetTopViewController()
    {
        var window = UIApplication.SharedApplication
            .ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(s => s.Windows)
            .FirstOrDefault(w => w.IsKeyWindow)
            ?? UIApplication.SharedApplication.Windows.FirstOrDefault(w => w.IsKeyWindow);

        var controller = window?.RootViewController;
        while (controller?.PresentedViewController is { } presented)
            controller = presented;
        return controller;
    }
}
