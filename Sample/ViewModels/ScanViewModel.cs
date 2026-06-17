using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shiny;
using Shiny.DocumentIntelligence;
using Sample.Pages;

namespace Sample.ViewModels;

[ShellMap<ScanPage>("Scan", registerRoute: false)]
public partial class ScanViewModel(IDocumentScanner scanner, IDialogs dialogs) : ObservableObject
{
    [ObservableProperty]
    public partial ObservableCollection<ImageSource> Pages { get; set; } = new();

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Tap Scan to capture a document.";

    [RelayCommand]
    async Task Scan()
    {
        if (!scanner.IsSupported)
        {
            await dialogs.Alert("Unsupported", "Document scanning isn't available on this device.");
            return;
        }

        try
        {
            var result = await scanner.ScanAsync(new DocumentScanRequest { PageLimit = 10 });
            if (result.IsCancelled)
            {
                this.StatusText = "Scan cancelled.";
                return;
            }

            var pages = new ObservableCollection<ImageSource>();
            foreach (var page in result.Pages)
            {
                var bytes = page.ImageData;
                pages.Add(ImageSource.FromStream(() => new MemoryStream(bytes)));
            }
            this.Pages = pages;
            this.StatusText = $"{result.Pages.Count} page(s) scanned.";
        }
        catch (Exception ex)
        {
            this.StatusText = $"Error: {ex.Message}";
        }
    }
}
