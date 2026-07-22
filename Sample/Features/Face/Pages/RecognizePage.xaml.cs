using Shiny.FaceIntelligence.Maui;

namespace Sample.Features.Face.Pages;

// FaceCameraView owns the camera, the permission, the lifecycle and the analyzer. The page just renders
// what comes out of it.
public partial class RecognizePage : ContentPage
{
    public RecognizePage() => this.InitializeComponent();

    void OnFaceRecognized(object? sender, FaceRecognizedEventArgs e)
        => (this.BindingContext as RecognizeViewModel)?.Show(e);

    void OnCameraFailed(object? sender, string reason)
    {
        if (this.BindingContext is RecognizeViewModel vm)
        {
            vm.ResultText = reason;
            vm.Note(reason);
        }
    }
}
