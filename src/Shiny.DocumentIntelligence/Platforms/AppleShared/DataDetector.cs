using Foundation;

namespace Shiny.DocumentIntelligence;

/// <summary>
/// <see cref="IDataDetector"/> over Foundation's <see cref="NSDataDetector"/> — the same engine that makes
/// dates and addresses tappable in Mail and Messages. Shared by iOS, Mac Catalyst and macOS.
/// </summary>
/// <remarks>
/// Worth using rather than regex: it is locale-aware, resolves partial dates against the current calendar,
/// and returns addresses already broken into street/city/state/postcode components. The detector instance is
/// created once and reused — construction compiles the rule set and is far more expensive than a match.
/// </remarks>
public class DataDetector : IDataDetector
{
    // NSDataDetector is documented as thread-safe for matching, and creating one is the costly part.
    static readonly Lazy<NSDataDetector?> Shared = new(Create, LazyThreadSafetyMode.ExecutionAndPublication);

    static NSDataDetector? Create()
    {
        // Only the *data* checking types are legal for NSDataDetector; spelling/grammar would throw.
        var types = NSTextCheckingType.Date | NSTextCheckingType.Address |
                    NSTextCheckingType.PhoneNumber | NSTextCheckingType.Link;

        var detector = NSDataDetector.Create((NSTextCheckingTypes)(ulong)types, out var error);
        return error is null ? detector : null;
    }

    public bool IsSupported => Shared.Value is not null;

    public IReadOnlyList<DetectedEntity> Detect(string text)
    {
        if (String.IsNullOrWhiteSpace(text) || Shared.Value is not { } detector)
            return [];

        var entities = new List<DetectedEntity>();
        using var ns = new NSString(text);

        // NSRange counts UTF-16 units and so does a C# string, so match ranges index `text` directly —
        // no re-encoding needed even when the OCR picked up emoji or other surrogate pairs.
        foreach (var match in detector.GetMatches(ns, 0, new NSRange(0, ns.Length)))
        {
            var start = (int)match.Range.Location;
            var length = (int)match.Range.Length;
            if (start < 0 || length <= 0 || start + length > text.Length)
                continue;

            var value = text.Substring(start, length);
            switch (match.ResultType)
            {
                case NSTextCheckingType.Date:
                    entities.Add(new DetectedEntity(DetectedEntityKind.Date, value, ToDateTimeOffset(match.Date)));
                    break;

                case NSTextCheckingType.Address:
                    entities.Add(new DetectedEntity(DetectedEntityKind.Address, value, Components: AddressComponents(match)));
                    break;

                case NSTextCheckingType.PhoneNumber:
                    entities.Add(new DetectedEntity(DetectedEntityKind.PhoneNumber, match.PhoneNumber ?? value));
                    break;

                case NSTextCheckingType.Link:
                    entities.Add(new DetectedEntity(DetectedEntityKind.Link, match.Url?.AbsoluteString ?? value));
                    break;
            }
        }

        return entities;
    }

    static DateTimeOffset? ToDateTimeOffset(NSDate? date)
        => date is null ? null : (DateTimeOffset)(DateTime)date;

    static IReadOnlyDictionary<string, string>? AddressComponents(NSTextCheckingResult match)
    {
        if (match.AddressComponents is not { } components)
            return null;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Take(string key, string? value)
        {
            if (!String.IsNullOrWhiteSpace(value))
                map[key] = value!;
        }

        Take("Name", components.Name);
        Take("JobTitle", components.JobTitle);
        Take("Organization", components.Organization);
        Take("Street", components.Street);
        Take("City", components.City);
        Take("State", components.State);
        Take("ZIP", components.ZIP);
        Take("Country", components.Country);
        Take("Phone", components.Phone);

        return map.Count > 0 ? map : null;
    }
}
