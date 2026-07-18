using Shiny.Maui.Controls.Camera;

namespace Sample.Features.Face.Pages;

// Camera hardware (permission/start/stop + capture) is a view concern and stays here. The face is no longer
// detected by a camera frame analyzer — we just capture a still on the button tap and hand the raw bytes to
// EnrollViewModel, where the ONNX detector finds (and quality-gates) the face.
public partial class EnrollPage : ContentPage
{
    bool busy;

    public EnrollPage() => this.InitializeComponent();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (await this.Camera.RequestPermissionAsync())
            await this.Camera.StartAsync();
        else if (this.BindingContext is EnrollViewModel vm)
            vm.StatusText = "Camera permission denied.";
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await this.Camera.StopAsync();
    }

    async void OnEnrollClicked(object? sender, EventArgs e)
    {
        if (this.BindingContext is not EnrollViewModel vm)
            return;
        if (this.busy)
            return;

        if (string.IsNullOrWhiteSpace(vm.Name))
        {
            vm.StatusText = "Enter a name first.";
            return;
        }

        this.busy = true;
        try
        {
            vm.StatusText = "Capturing photo…";
            var photo = await this.Camera.CapturePhotoAsync();
            // The detector locates + quality-checks the face; the VM turns any rejection into a message.
            await vm.Process(photo.Data);
        }
        catch (Exception ex)
        {
            vm.StatusText = $"Capture failed ({ex.GetType().Name}): {ex.Message}";
        }
        finally
        {
            this.busy = false;
        }
    }
}
