using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shiny;
using Shiny.DocumentIntelligence;
using Sample.Pages;

namespace Sample.ViewModels;

[ShellMap<ScanPage>("Scan", registerRoute: false)]
public partial class ScanViewModel(IDocumentScanner scanner, IDocumentExtractor extractor, IDialogs dialogs) : ObservableObject
{
    [ObservableProperty]
    public partial ObservableCollection<ImageSource> Pages { get; set; } = new();

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Pick a document type and tap Scan.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExtractedText))]
    public partial string? ExtractedText { get; set; }

    public bool HasExtractedText => !String.IsNullOrWhiteSpace(this.ExtractedText);

    /// <summary>The document types the user can extract. Bound to a Picker in the page.</summary>
    public IReadOnlyList<DocumentType> DocumentTypes { get; } =
        [DocumentType.Receipt, DocumentType.Invoice, DocumentType.DriversLicense, DocumentType.Passport];

    [ObservableProperty]
    public partial DocumentType SelectedDocumentType { get; set; } = DocumentType.Receipt;

    [RelayCommand]
    async Task Scan()
    {
        if (!scanner.IsSupported)
        {
            await dialogs.Alert("Unsupported", "Document scanning isn't available on this device.");
            return;
        }

        this.ExtractedText = null;
        try
        {
            // The license barcode lives on the back, so allow a couple of pages for it.
            var result = await scanner.ScanAsync(new DocumentScanRequest { PageLimit = 5 });
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
            this.StatusText = $"{result.Pages.Count} page(s) scanned. Extracting {this.SelectedDocumentType}…";

            // Second stage: turn the captured images into structured fields on-device.
            var extracted = await extractor.ExtractAsync(result, this.SelectedDocumentType);
            this.ExtractedText = Format(extracted);
            this.StatusText = extracted.HasStructuredData
                ? $"Extracted {this.SelectedDocumentType} fields."
                : $"Scanned, but couldn't read {this.SelectedDocumentType} fields. Showing raw text.";
        }
        catch (PlatformNotSupportedException)
        {
            this.StatusText = "On-device extraction isn't available on this platform.";
        }
        catch (Exception ex)
        {
            this.StatusText = $"Error: {ex.Message}";
        }
    }

    static string Format(ExtractedDocument doc)
    {
        var sb = new StringBuilder();
        switch (doc)
        {
            case { Receipt: { } r }:
                Add(sb, "Merchant", r.Merchant);
                Add(sb, "Date", r.Date?.ToString("yyyy-MM-dd"));
                Add(sb, "Subtotal", Money(r.Subtotal, r.Currency));
                Add(sb, "Tax", Money(r.Tax, r.Currency));
                Add(sb, "Total", Money(r.Total, r.Currency));
                AddItems(sb, r.Items);
                break;

            case { Invoice: { } i }:
                Add(sb, "Vendor", i.Vendor);
                Add(sb, "Invoice #", i.InvoiceNumber);
                Add(sb, "Invoice Date", i.InvoiceDate?.ToString("yyyy-MM-dd"));
                Add(sb, "Due Date", i.DueDate?.ToString("yyyy-MM-dd"));
                Add(sb, "Subtotal", Money(i.Subtotal, i.Currency));
                Add(sb, "Tax", Money(i.Tax, i.Currency));
                Add(sb, "Total", Money(i.Total, i.Currency));
                AddItems(sb, i.Items);
                break;

            case { License: { } l }:
                Add(sb, "Name", String.Join(' ', new[] { l.FirstName, l.MiddleName, l.LastName }.Where(s => !String.IsNullOrEmpty(s))));
                Add(sb, "License #", l.LicenseNumber);
                Add(sb, "Date of Birth", l.DateOfBirth?.ToString("yyyy-MM-dd"));
                Add(sb, "Expires", l.ExpiryDate?.ToString("yyyy-MM-dd"));
                Add(sb, "Sex", l.Sex);
                Add(sb, "Address", String.Join(", ", new[] { l.Address, l.City, l.State, l.PostalCode }.Where(s => !String.IsNullOrEmpty(s))));
                break;

            case { Passport: { } p }:
                Add(sb, "Surname", p.Surname);
                Add(sb, "Given Names", p.GivenNames);
                Add(sb, "Passport #", p.PassportNumber);
                Add(sb, "Nationality", p.Nationality);
                Add(sb, "Date of Birth", p.DateOfBirth?.ToString("yyyy-MM-dd"));
                Add(sb, "Sex", p.Sex);
                Add(sb, "Expires", p.ExpiryDate?.ToString("yyyy-MM-dd"));
                Add(sb, "MRZ valid", p.IsValid ? "yes" : "no (check digits failed)");
                break;
        }

        if (sb.Length == 0 && !String.IsNullOrWhiteSpace(doc.RawText))
        {
            sb.AppendLine("Raw text:");
            sb.Append(doc.RawText);
        }
        return sb.ToString().TrimEnd();
    }

    static void Add(StringBuilder sb, string label, string? value)
    {
        if (!String.IsNullOrWhiteSpace(value))
            sb.AppendLine($"{label}: {value}");
    }

    static void AddItems(StringBuilder sb, IReadOnlyList<LineItem> items)
    {
        if (items.Count == 0)
            return;
        sb.AppendLine($"Items ({items.Count}):");
        foreach (var item in items)
            sb.AppendLine($"  • {item.Description}{(item.Amount is { } a ? $" — {a:0.00}" : "")}");
    }

    static string? Money(decimal? amount, string? currency) =>
        amount is null ? null : $"{amount:0.00}{(currency is null ? "" : $" {currency}")}";
}
