using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shiny;
using Shiny.DocumentIntelligence;

namespace Sample.Features.Documents.Pages;

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
        [DocumentType.Receipt, DocumentType.Invoice, DocumentType.DriversLicense, DocumentType.Passport, DocumentType.CreditCard];

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

            DumpPages(result);

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

    /// <summary>
    /// Write each scanned page to the app container so it can be pulled off-device and fed to the OCR
    /// directly. When extraction returns nothing, the only way to tell a bad capture from a bad recognizer
    /// setting from a bad parser is to look at the image the pipeline actually received.
    /// </summary>
    [System.Diagnostics.Conditional("DEBUG")]
    static void DumpPages(DocumentScanResult scan)
    {
        try
        {
            var dir = Path.Combine(FileSystem.AppDataDirectory, "scans");
            Directory.CreateDirectory(dir);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            for (var i = 0; i < scan.Pages.Count; i++)
            {
                var path = Path.Combine(dir, $"scan-{stamp}-p{i + 1}.png");
                File.WriteAllBytes(path, scan.Pages[i].ImageData);
                Console.WriteLine($"[Scan] wrote {path} ({scan.Pages[i].ImageData.Length / 1024} KB)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Scan] page dump failed: {ex.Message}");
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
                Add(sb, "Currency", r.Currency);
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
                Add(sb, "Currency", i.Currency);
                AddItems(sb, i.Items);
                break;

            case { License: { } l }:
                Add(sb, "Name", String.Join(' ', new[] { l.FirstName, l.MiddleName, l.LastName }.Where(s => !String.IsNullOrEmpty(s))));
                Add(sb, "License #", l.LicenseNumber);
                Add(sb, "Date of Birth", l.DateOfBirth?.ToString("yyyy-MM-dd"));
                Add(sb, "Issued", l.IssueDate?.ToString("yyyy-MM-dd"));
                Add(sb, "Expires", l.ExpiryDate?.ToString("yyyy-MM-dd"));
                Add(sb, "Sex", l.Sex);
                Add(sb, "Address", String.Join(", ", new[] { l.Address, l.City, l.State, l.PostalCode }.Where(s => !String.IsNullOrEmpty(s))));
                AddRemainingElements(sb, l);
                break;

            case { Passport: { } p }:
                Add(sb, "Document Code", p.DocumentCode);
                Add(sb, "Issuing Country", p.IssuingCountry);
                Add(sb, "Surname", p.Surname);
                Add(sb, "Given Names", p.GivenNames);
                Add(sb, "Passport #", p.PassportNumber);
                Add(sb, "Nationality", p.Nationality);
                Add(sb, "Date of Birth", p.DateOfBirth?.ToString("yyyy-MM-dd"));
                Add(sb, "Sex", p.Sex);
                Add(sb, "Expires", p.ExpiryDate?.ToString("yyyy-MM-dd"));
                Add(sb, "Personal #", p.PersonalNumber);
                Add(sb, "MRZ valid", p.IsValid ? "yes" : "no (check digits failed)");
                break;

            case { CreditCard: { } c }:
                // MASKED on purpose. This is a demo screen; showing a full PAN on-screen (and therefore in
                // any screenshot or screen recording of it) is exactly the habit a payments app must not
                // have. c.Number holds the full value if a real integration needs it.
                Add(sb, "Card", c.MaskedNumber);
                Add(sb, "Network", c.Network.ToString());
                Add(sb, "Expires", c.ExpiryMonth is { } m && c.ExpiryYear is { } y ? $"{m:00}/{y}" : null);
                Add(sb, "Cardholder", c.CardholderName);
                Add(sb, "Luhn valid", c.IsValid ? "yes" : "no (check digit failed — likely a misread)");
                if (c.ExpiresOn is { } exp)
                    Add(sb, "Status", c.IsExpired(DateOnly.FromDateTime(DateTime.Today)) ? $"EXPIRED ({exp:yyyy-MM-dd})" : $"valid to {exp:yyyy-MM-dd}");
                break;
        }

        AddEntities(sb, doc.Entities);

        // The raw OCR/barcode text always follows the structured fields. When a parser returns nothing it's
        // the only output; when it returns something it's how you tell a parsing miss from an OCR miss.
        sb.AppendLine();
        sb.AppendLine(sb.Length == 0 ? "Raw text:" : $"Raw text ({doc.RawText.Length} chars):");
        sb.Append(String.IsNullOrWhiteSpace(doc.RawText) ? "—" : doc.RawText);

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// The AAMVA elements that aren't already surfaced as named fields. The barcode carries far more than
    /// the handful mapped onto <see cref="LicenseData"/>'s properties (height, eye colour, endorsements,
    /// restrictions…), and a demo of an extraction library should show what was actually extracted.
    /// </summary>
    static void AddRemainingElements(StringBuilder sb, LicenseData license)
    {
        // Element IDs already shown above as named properties.
        string[] mapped = ["DAC", "DAD", "DCS", "DBB", "DAQ", "DBD", "DBA", "DBC", "DAG", "DAI", "DAJ", "DAK"];
        var rest = license.Elements
            .Where(kv => !mapped.Contains(kv.Key) && !String.IsNullOrWhiteSpace(kv.Value))
            .OrderBy(kv => kv.Key)
            .ToList();

        if (rest.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"Other AAMVA elements ({rest.Count}):");
        foreach (var kv in rest)
            sb.AppendLine($"  {kv.Key}: {kv.Value}");
    }

    /// <summary>
    /// Print a field <b>always</b>, with an em dash when the parser didn't get it. This is a demo of an
    /// extraction library, so what it <i>failed</i> to read is as informative as what it read — silently
    /// omitting empty fields makes a partial parse look like a complete one.
    /// </summary>
    static void Add(StringBuilder sb, string label, string? value)
        => sb.AppendLine($"{label}: {(String.IsNullOrWhiteSpace(value) ? "—" : value)}");

    /// <summary>
    /// Dates/addresses/phones/links the platform data detector found. Apple-only today, so this section is
    /// simply absent elsewhere — which is itself worth seeing when comparing platforms.
    /// </summary>
    static void AddEntities(StringBuilder sb, IReadOnlyList<DetectedEntity> entities)
    {
        sb.AppendLine();
        if (entities.Count == 0)
        {
            sb.AppendLine("Detected entities: — (no platform data detector, or nothing found)");
            return;
        }

        sb.AppendLine($"Detected entities ({entities.Count}):");
        foreach (var e in entities)
        {
            var detail = e.Kind == DetectedEntityKind.Date && e.Date is { } d ? $" → {d:yyyy-MM-dd}" : String.Empty;
            sb.AppendLine($"  {e.Kind}: {e.Value}{detail}");
            if (e.Components is { Count: > 0 } parts)
                foreach (var kv in parts)
                    sb.AppendLine($"      {kv.Key}: {kv.Value}");
        }
    }

    static void AddItems(StringBuilder sb, IReadOnlyList<LineItem> items)
    {
        if (items.Count == 0)
        {
            sb.AppendLine("Items: —");
            return;
        }
        sb.AppendLine($"Items ({items.Count}):");
        foreach (var item in items)
            sb.AppendLine($"  • {item.Description}{(item.Amount is { } a ? $" — {a:0.00}" : "")}");
    }

    static string? Money(decimal? amount, string? currency) =>
        amount is null ? null : $"{amount:0.00}{(currency is null ? "" : $" {currency}")}";
}
