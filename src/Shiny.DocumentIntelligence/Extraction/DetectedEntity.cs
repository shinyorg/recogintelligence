namespace Shiny.DocumentIntelligence;

/// <summary>The kind of thing an <see cref="IDataDetector"/> found in recognized text.</summary>
public enum DetectedEntityKind
{
    /// <summary>A date, and optionally a time. <see cref="DetectedEntity.Date"/> carries the resolved value.</summary>
    Date,

    /// <summary>A postal address. <see cref="DetectedEntity.Components"/> carries street/city/state/postcode when available.</summary>
    Address,

    /// <summary>A telephone number.</summary>
    PhoneNumber,

    /// <summary>A URL or email address.</summary>
    Link
}

/// <summary>
/// One entity located in recognized text by the platform's data detector.
/// </summary>
/// <param name="Kind">What was found.</param>
/// <param name="Value">The matched text exactly as it appeared.</param>
/// <param name="Date">Resolved date for <see cref="DetectedEntityKind.Date"/>; null otherwise.</param>
/// <param name="Components">
/// Structured parts, currently only for addresses (keys such as <c>Street</c>, <c>City</c>, <c>State</c>,
/// <c>ZIP</c>, <c>Country</c>). Empty when the platform didn't break the match down.
/// </param>
public record DetectedEntity(
    DetectedEntityKind Kind,
    string Value,
    DateTimeOffset? Date = null,
    IReadOnlyDictionary<string, string>? Components = null
);
