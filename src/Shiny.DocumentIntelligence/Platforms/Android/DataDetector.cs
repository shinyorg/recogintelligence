namespace Shiny.DocumentIntelligence;

/// <summary>
/// No-op <see cref="IDataDetector"/> on Android — there is no framework equivalent of Apple's
/// <c>NSDataDetector</c>.
/// </summary>
/// <remarks>
/// ML Kit's Entity Extraction would be the closest match and could be dropped in behind this same interface,
/// but it pulls another Play Services dependency and downloads a per-language model at runtime, so it isn't
/// on by default. Entity detection is pure enrichment: with this inert, the managed parsers behave exactly
/// as they do everywhere else.
/// </remarks>
public class DataDetector : IDataDetector
{
    public bool IsSupported => false;

    public IReadOnlyList<DetectedEntity> Detect(string text) => [];
}
