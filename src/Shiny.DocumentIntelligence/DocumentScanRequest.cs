namespace Shiny.DocumentIntelligence;

/// <summary>Options for a scan. All platforms honor what they can and ignore the rest.</summary>
public class DocumentScanRequest
{
    /// <summary>Maximum number of pages the user may capture. Default 10. (Android enforces this natively.)</summary>
    public int PageLimit { get; set; } = 10;

    /// <summary>Allow importing from the photo gallery in addition to the camera, where the platform offers it (Android).</summary>
    public bool AllowGalleryImport { get; set; } = true;

    /// <summary>Which artifacts to produce. Default <see cref="DocumentScanFormats.Image"/>.</summary>
    public DocumentScanFormats Formats { get; set; } = DocumentScanFormats.Image;
}
