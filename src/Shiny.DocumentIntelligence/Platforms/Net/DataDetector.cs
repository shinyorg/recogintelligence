namespace Shiny.DocumentIntelligence;

/// <summary>
/// No-op <see cref="IDataDetector"/> for platforms without a system data detector (bare net10.0 / Windows).
/// </summary>
/// <remarks>
/// Deliberately inert rather than a managed reimplementation: entity detection is pure enrichment, and the
/// managed parsers already extract what they need from the text on their own. A half-good stand-in would
/// silently change parse results between platforms, which is worse than not having it.
/// </remarks>
public class DataDetector : IDataDetector
{
    public bool IsSupported => false;

    public IReadOnlyList<DetectedEntity> Detect(string text) => [];
}
