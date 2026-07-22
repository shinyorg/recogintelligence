using Shiny.FaceIntelligence.Maui;

namespace Sample.Features.Face.Pages;

// The control owns the camera, the prompt sequence and all the capture gating; the page just starts it
// and reports the outcome.
public partial class EnrollPage : ContentPage
{
    public EnrollPage() => this.InitializeComponent();

    void OnStartClicked(object? sender, EventArgs e)
    {
        if (this.BindingContext is not EnrollViewModel vm)
            return;

        if (String.IsNullOrWhiteSpace(vm.Name))
        {
            vm.StatusText = "Enter a name first.";
            return;
        }

        vm.StartEnrollment(this.FaceCamera);
    }

    void OnCompleted(object? sender, FaceEnrollmentResult e)
        => (this.BindingContext as EnrollViewModel)?.Show(e);

    void OnCameraFailed(object? sender, string reason)
    {
        if (this.BindingContext is EnrollViewModel vm)
            vm.StatusText = reason;
    }
}
