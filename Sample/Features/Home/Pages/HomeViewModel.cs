using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shiny;

namespace Sample.Features.Home.Pages;

// Declared in AppShell.xaml as the root ShellContent, so registerRoute: false.
// Each button jumps to an area via absolute navigation (relativeNavigation: false → "//<route>"),
// landing on the first tab of the target TabBar (or the Scan page).
[ShellMap<HomePage>("Home", registerRoute: false)]
public partial class HomeViewModel(INavigator navigator) : ObservableObject
{
    [RelayCommand]
    Task Face() => navigator.NavigateTo("Recognize", relativeNavigation: false);

    [RelayCommand]
    Task Voice() => navigator.NavigateTo("VoiceRecognize", relativeNavigation: false);

    [RelayCommand]
    Task Scan() => navigator.NavigateTo("Scan", relativeNavigation: false);
}
