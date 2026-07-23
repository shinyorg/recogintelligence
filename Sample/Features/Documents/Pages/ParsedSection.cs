namespace Sample.Features.Documents.Pages;

/// <summary>
/// One group of parsed values on the Scan page. <paramref name="TypeName"/> is the actual .NET type the
/// extractor produced — the whole point of the screen is to show what the library parsed the image <i>to</i>,
/// not just the text it read off it.
/// </summary>
public record ParsedSection(string Title, string? TypeName, IReadOnlyList<ParsedField> Fields)
{
    public bool HasTypeName => !String.IsNullOrEmpty(this.TypeName);
}

/// <summary>
/// One property of a parsed payload.
/// </summary>
/// <param name="Label">The property, in human form.</param>
/// <param name="Value">Its value, or null when the parser didn't get one.</param>
/// <param name="IsWarning">Renders in red: a failed checksum, an expired card, nothing parsed at all.</param>
/// <param name="IsDetail">Renders indented: a sub-value, such as an address component of a detected entity.</param>
/// <remarks>
/// A field renders <b>always</b>, even when null — what the parser failed to read is as informative as what it
/// read, and silently dropping empty fields makes a partial parse look like a complete one.
/// </remarks>
public record ParsedField(string Label, string? Value, bool IsWarning = false, bool IsDetail = false)
{
    public string Display => String.IsNullOrWhiteSpace(this.Value) ? "—" : this.Value!;

    public bool IsMissing => String.IsNullOrWhiteSpace(this.Value);

    public Thickness IndentMargin => this.IsDetail ? new Thickness(16, 0, 0, 0) : Thickness.Zero;
}
