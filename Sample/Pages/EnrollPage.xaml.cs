using Sample.ViewModels;
using Shiny.Maui.Controls.Camera;
using Shiny.Maui.Controls.Camera.Face;

namespace Sample.Pages;

// Camera hardware (permission/start/stop + capture) is a view concern and stays here.
// Enrollment logic (embed + store) lives in EnrollViewModel.
public partial class EnrollPage : ContentPage
{
    IReadOnlyList<DetectedFace> latestFaces = [];
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

    // Just remember the most recent detection; capture happens on the button tap.
    void OnFacesDetected(object? sender, FacesDetectedEventArgs e) => this.latestFaces = e.Faces;

    // Capture on an explicit tap (not buried in the per-frame detection event). Every step updates the
    // status label, so whatever message is on screen when it stops tells us exactly where it failed.
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

        var face = this.latestFaces.Largest();
        if (face is null)
        {
            vm.StatusText = "No face detected yet — line up your face in the preview, then tap again.";
            return;
        }

        this.busy = true;
        try
        {
            vm.StatusText = "Capturing photo…";
            var photo = await this.Camera.CapturePhotoAsync();

            vm.StatusText = $"Captured {photo.Width}×{photo.Height}, box=({face.Bounds.X:F2},{face.Bounds.Y:F2},{face.Bounds.Width:F2},{face.Bounds.Height:F2}) — enrolling…";
            await vm.Process(photo.Data, face.ToFaceBox(photo.Width, photo.Height));
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
