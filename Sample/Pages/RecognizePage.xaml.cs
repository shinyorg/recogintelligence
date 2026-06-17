using Sample.ViewModels;
using Shiny.Maui.Controls.Camera;
using Shiny.Maui.Controls.Camera.Face;

namespace Sample.Pages;

// Camera hardware (permission/start/stop) and frame capture are view concerns and stay here.
// All recognition logic lives in RecognizeViewModel, assigned as BindingContext by Shiny Shell.
public partial class RecognizePage : ContentPage
{
    public RecognizePage() => this.InitializeComponent();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (await this.Camera.RequestPermissionAsync())
            await this.Camera.StartAsync();
        else if (this.BindingContext is RecognizeViewModel vm)
            vm.ResultText = "Camera permission denied.";
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await this.Camera.StopAsync();
    }

    async void OnDetectionCaptured(object? sender, DetectionCapturedEventArgs e)
    {
        if (this.BindingContext is not RecognizeViewModel vm)
            return;
        if (e.Detection is not FacesDetectedEventArgs fe || e.Photo is not { } photo)
            return;

        var face = fe.Faces.Largest();
        if (face is null)
            return;

        await vm.Process(photo.Data, face.ToFaceBox());
    }
}
