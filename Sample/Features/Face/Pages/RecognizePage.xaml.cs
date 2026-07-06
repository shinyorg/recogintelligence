using Shiny.Maui.Controls.Camera;
using Shiny.Maui.Controls.Camera.Face;

namespace Sample.Features.Face.Pages;

// Camera hardware (permission/start/stop) and frame capture are view concerns and stay here.
// All recognition logic lives in RecognizeViewModel, assigned as BindingContext by Shiny Shell.
public partial class RecognizePage : ContentPage
{
    bool capturing;

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

    // Capture is driven off FacesDetected (the CaptureOnDetection/DetectionCaptured path doesn't fire on
    // iOS in this beta). Recognition is continuous; the capturing guard + the VM's busy flag pace it to
    // one in-flight cycle so stills don't pile up.
    async void OnFacesDetected(object? sender, FacesDetectedEventArgs e)
    {
        if (this.BindingContext is not RecognizeViewModel vm)
            return;
        if (this.capturing)
            return;

        var face = e.Faces.Largest();
        if (face is null)
            return;

        this.capturing = true;
        try
        {
            var photo = await this.Camera.CapturePhotoAsync();
            await vm.Process(photo.Data, face.ToFaceBox(photo.Width, photo.Height));
        }
        catch
        {
            // Transient capture error; the next detected frame retries.
        }
        finally
        {
            this.capturing = false;
        }
    }
}
