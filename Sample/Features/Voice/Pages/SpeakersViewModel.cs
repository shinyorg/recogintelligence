using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shiny;
using Shiny.VoiceIntelligence;

namespace Sample.Features.Voice.Pages;

/// <summary>One row per enrolled speaker name (utterances collapsed into a single entry).</summary>
public record SpeakerRow(string Name, int Count)
{
    public string CountText => this.Count == 1 ? "1 sample" : $"{this.Count} samples";
}

[ShellMap<SpeakersPage>("Speakers", registerRoute: false)]
public partial class SpeakersViewModel(IVoiceIntelligence voice, IDialogs dialogs) : ObservableObject, IPageLifecycleAware
{
    [ObservableProperty]
    public partial ObservableCollection<SpeakerRow> Speakers { get; set; } = new();

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    public async void OnAppearing() => await this.Reload();

    public void OnDisappearing() { }

    async Task Reload()
    {
        try
        {
            var speakers = await voice.GetAll();
            var rows = speakers
                .GroupBy(s => s.PersonIdentifier)   // the Sample uses the person's name as the identifier
                .Select(g => new SpeakerRow(g.Key, g.Count()))
                .OrderBy(r => r.Name)
                .ToList();

            this.Speakers = new ObservableCollection<SpeakerRow>(rows);
            this.StatusText = $"{rows.Count} enrolled · {speakers.Count} sample(s)";
        }
        catch (Exception ex)
        {
            this.StatusText = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    async Task Forget(string name)
    {
        if (!await dialogs.Confirm("Forget", $"Remove all samples of '{name}'?", "Forget", "Cancel"))
            return;

        await voice.Forget(name);
        await this.Reload();
    }
}
