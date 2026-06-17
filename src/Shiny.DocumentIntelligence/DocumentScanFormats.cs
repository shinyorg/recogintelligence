namespace Shiny.DocumentIntelligence;

/// <summary>Which artifacts to request from the scan. Image is always produced; PDF is honored where supported (Android).</summary>
[Flags]
public enum DocumentScanFormats
{
    /// <summary>One image per page (PNG/JPEG). Always available.</summary>
    Image = 1,

    /// <summary>A single multi-page PDF of the scan. Android only; ignored elsewhere.</summary>
    Pdf = 2
}
