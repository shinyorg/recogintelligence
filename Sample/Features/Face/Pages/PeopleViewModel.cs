using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shiny;
using Shiny.FaceIntelligence;

namespace Sample.Features.Face.Pages;

[ShellMap<PeoplePage>("People", registerRoute: false)]
public partial class PeopleViewModel(IFaceIntelligence recognizer, IDialogs dialogs) : ObservableObject, IPageLifecycleAware
{
    [ObservableProperty]
    public partial ObservableCollection<PersonRow> People { get; set; } = new();

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    public async void OnAppearing() => await this.Reload();

    public void OnDisappearing() { }

    async Task Reload()
    {
        try
        {
            var people = await recognizer.GetAll();
            var rows = people
                .GroupBy(p => p.PersonIdentifier)   // the Sample uses the person's name as the identifier
                .Select(g =>
                {
                    var withThumb = g.FirstOrDefault(p => p.Thumbnail is { Length: > 0 });
                    ImageSource? thumb = withThumb?.Thumbnail is { } bytes
                        ? ImageSource.FromStream(() => new MemoryStream(bytes))
                        : null;
                    return new PersonRow(g.Key, g.Count(), thumb);
                })
                .OrderBy(r => r.Name)
                .ToList();

            this.People = new ObservableCollection<PersonRow>(rows);
            this.StatusText = $"{rows.Count} enrolled · {people.Count} shot(s)";
        }
        catch (Exception ex)
        {
            this.StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    async Task Forget(string name)
    {
        if (!await dialogs.Confirm("Forget", $"Remove all shots of '{name}'?", "Forget", "Cancel"))
            return;

        await recognizer.Forget(name);
        await this.Reload();
    }
}
