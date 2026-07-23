using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shiny;
using Shiny.DocumentIntelligence;

namespace Sample.Features.Documents.Pages;

[ShellMap<ScanPage>("Scan", registerRoute: false)]
public partial class ScanViewModel(IDocumentScanner scanner, IDocumentExtractor extractor, IDialogs dialogs) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPages))]
    public partial ObservableCollection<ImageSource> Pages { get; set; } = new();

    public bool HasPages => this.Pages.Count > 0;

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Pick a document type and tap Scan.";

    /// <summary>The typed result, grouped for display. Replaced wholesale on each scan.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSections), nameof(HasNoSections))]
    public partial IReadOnlyList<ParsedSection> Sections { get; set; } = [];

    public bool HasSections => this.Sections.Count > 0;

    /// <summary>Exists so the XAML can show the empty state without an inverse-bool converter.</summary>
    public bool HasNoSections => this.Sections.Count == 0;

    /// <summary>
    /// The raw OCR/barcode text, behind a toggle. It's demoted rather than dropped: when a field comes back
    /// empty, the raw text is how you tell a parsing miss from an OCR miss.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRawText), nameof(RawTextButtonText))]
    public partial string? RawText { get; set; }

    public bool HasRawText => !String.IsNullOrWhiteSpace(this.RawText);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RawTextButtonText))]
    public partial bool ShowRawText { get; set; }

    public string RawTextButtonText => this.ShowRawText
        ? "Hide raw text"
        : $"Show raw text ({this.RawText?.Length ?? 0} chars)";

    [RelayCommand]
    void ToggleRawText() => this.ShowRawText = !this.ShowRawText;

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

        this.Sections = [];
        this.RawText = null;
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
            this.Sections = BuildSections(extracted);
            this.RawText = extracted.RawText;
            // Nothing parsed means the raw text is the only thing to look at, so open it unasked.
            this.ShowRawText = !extracted.HasStructuredData;
            this.StatusText = extracted.HasStructuredData
                ? $"Extracted {this.SelectedDocumentType} fields."
                : $"Scanned, but couldn't read {this.SelectedDocumentType} fields.";
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

    /// <summary>
    /// Projects the extractor's output into display sections. Every payload property is listed by hand rather
    /// than reflected over: the app is AOT-compatible, and an explicit list is the thing that makes a newly
    /// added field an obvious compile-time-adjacent chore. <b>If you add a field to a payload record, add it
    /// here too.</b>
    /// </summary>
    static IReadOnlyList<ParsedSection> BuildSections(ExtractedDocument doc)
    {
        var sections = new List<ParsedSection>
        {
            new("Extracted document", nameof(ExtractedDocument), [
                new("Requested type", doc.Type.ToString()),
                new("Structured data", doc.HasStructuredData ? "yes" : "no", IsWarning: !doc.HasStructuredData),
                new("Raw text", $"{doc.RawText.Length} chars")
            ])
        };

        switch (doc)
        {
            case { Receipt: { } r }:
                sections.Add(new ParsedSection("Receipt", nameof(ReceiptData), [
                    new("Merchant", r.Merchant),
                    new("Date", r.Date?.ToString("yyyy-MM-dd")),
                    new("Subtotal", Money(r.Subtotal, r.Currency)),
                    new("Tax", Money(r.Tax, r.Currency)),
                    new("Total", Money(r.Total, r.Currency)),
                    new("Currency", r.Currency)
                ]));
                sections.Add(ItemsSection(r.Items));
                break;

            case { Invoice: { } i }:
                sections.Add(new ParsedSection("Invoice", nameof(InvoiceData), [
                    new("Vendor", i.Vendor),
                    new("Invoice #", i.InvoiceNumber),
                    new("Invoice date", i.InvoiceDate?.ToString("yyyy-MM-dd")),
                    new("Due date", i.DueDate?.ToString("yyyy-MM-dd")),
                    new("Subtotal", Money(i.Subtotal, i.Currency)),
                    new("Tax", Money(i.Tax, i.Currency)),
                    new("Total", Money(i.Total, i.Currency)),
                    new("Currency", i.Currency)
                ]));
                sections.Add(ItemsSection(i.Items));
                break;

            case { License: { } l }:
                sections.Add(new ParsedSection("Driver's licence", nameof(LicenseData), [
                    new("Name", Join(' ', l.FirstName, l.MiddleName, l.LastName)),
                    new("Licence #", l.LicenseNumber),
                    new("Date of birth", l.DateOfBirth?.ToString("yyyy-MM-dd")),
                    new("Issued", l.IssueDate?.ToString("yyyy-MM-dd")),
                    new("Expires", l.ExpiryDate?.ToString("yyyy-MM-dd")),
                    new("Sex", l.Sex),
                    new("Address", Join(", ", l.Address, l.City, l.State, l.PostalCode))
                ]));
                if (RemainingElementsSection(l) is { } extras)
                    sections.Add(extras);
                break;

            case { Passport: { } p }:
                sections.Add(new ParsedSection("Passport", nameof(PassportData), [
                    new("Document code", p.DocumentCode),
                    new("Issuing country", p.IssuingCountry),
                    new("Surname", p.Surname),
                    new("Given names", p.GivenNames),
                    new("Passport #", p.PassportNumber),
                    new("Nationality", p.Nationality),
                    new("Date of birth", p.DateOfBirth?.ToString("yyyy-MM-dd")),
                    new("Sex", p.Sex),
                    new("Expires", p.ExpiryDate?.ToString("yyyy-MM-dd")),
                    new("Personal #", p.PersonalNumber),
                    new("MRZ check digits", p.IsValid ? "valid" : "failed — likely a misread", IsWarning: !p.IsValid)
                ]));
                break;

            case { CreditCard: { } c }:
                // MASKED on purpose. This is a demo screen; showing a full PAN on-screen (and therefore in
                // any screenshot or screen recording of it) is exactly the habit a payments app must not
                // have. c.Number holds the full value if a real integration needs it.
                sections.Add(new ParsedSection("Credit card", nameof(CreditCardData), [
                    new("Card", c.MaskedNumber),
                    new("Network", c.Network.ToString()),
                    new("Expires", c.ExpiryMonth is { } m && c.ExpiryYear is { } y ? $"{m:00}/{y}" : null),
                    new("Cardholder", c.CardholderName),
                    new("Luhn check digit", c.IsValid ? "valid" : "failed — likely a misread", IsWarning: !c.IsValid),
                    ExpiryStatus(c)
                ]));
                break;
        }

        sections.Add(EntitiesSection(doc.Entities));
        return sections;
    }

    static ParsedField ExpiryStatus(CreditCardData card)
    {
        if (card.ExpiresOn is not { } expiry)
            return new ParsedField("Status", null);

        var expired = card.IsExpired(DateOnly.FromDateTime(DateTime.Today));
        return new ParsedField("Status", expired ? $"EXPIRED ({expiry:yyyy-MM-dd})" : $"valid to {expiry:yyyy-MM-dd}", IsWarning: expired);
    }

    static ParsedSection ItemsSection(IReadOnlyList<LineItem> items) =>
        new(
            $"Line items ({items.Count})",
            $"{nameof(LineItem)}[]",
            items.Count == 0
                ? [new ParsedField("Items", null)]
                : items.Select(i => new ParsedField(i.Description, i.Amount?.ToString("0.00"))).ToList()
        );

    /// <summary>
    /// The AAMVA elements that aren't already surfaced as named fields. The barcode carries far more than
    /// the handful mapped onto <see cref="LicenseData"/>'s properties (height, eye colour, endorsements,
    /// restrictions…), and a demo of an extraction library should show what was actually extracted.
    /// </summary>
    static ParsedSection? RemainingElementsSection(LicenseData license)
    {
        // Element IDs already shown above as named properties.
        string[] mapped = ["DAC", "DAD", "DCS", "DBB", "DAQ", "DBD", "DBA", "DBC", "DAG", "DAI", "DAJ", "DAK"];
        var rest = license.Elements
            .Where(kv => !mapped.Contains(kv.Key) && !String.IsNullOrWhiteSpace(kv.Value))
            .OrderBy(kv => kv.Key)
            .Select(kv => new ParsedField(kv.Key, kv.Value))
            .ToList();

        return rest.Count == 0 ? null : new ParsedSection($"Other AAMVA elements ({rest.Count})", "Elements", rest);
    }

    /// <summary>
    /// Dates/addresses/phones/links the platform data detector found. Apple-only today, so elsewhere this
    /// section reports nothing found — which is itself worth seeing when comparing platforms.
    /// </summary>
    static ParsedSection EntitiesSection(IReadOnlyList<DetectedEntity> entities)
    {
        if (entities.Count == 0)
            return new ParsedSection("Detected entities (0)", $"{nameof(DetectedEntity)}[]", [
                new ParsedField("Entities", "none — no platform data detector, or nothing matched")
            ]);

        var fields = new List<ParsedField>();
        foreach (var e in entities)
        {
            var detail = e.Kind == DetectedEntityKind.Date && e.Date is { } d ? $" → {d:yyyy-MM-dd}" : String.Empty;
            fields.Add(new ParsedField(e.Kind.ToString(), $"{e.Value}{detail}"));
            if (e.Components is { Count: > 0 } parts)
                fields.AddRange(parts.Select(kv => new ParsedField(kv.Key, kv.Value, IsDetail: true)));
        }
        return new ParsedSection($"Detected entities ({entities.Count})", $"{nameof(DetectedEntity)}[]", fields);
    }

    static string? Join(char separator, params string?[] parts) =>
        String.Join(separator, parts.Where(s => !String.IsNullOrEmpty(s)));

    static string? Join(string separator, params string?[] parts) =>
        String.Join(separator, parts.Where(s => !String.IsNullOrEmpty(s)));

    static string? Money(decimal? amount, string? currency) =>
        amount is null ? null : $"{amount:0.00}{(currency is null ? "" : $" {currency}")}";
}
