using System.Text.Json.Serialization;

namespace Shiny.VoiceIntelligence;

/// <summary>
/// An enrolled voice. One <see cref="Speaker"/> document = one captured utterance of someone, holding the
/// L2-normalized voiceprint used for vector search. Enroll several utterances per name for a robust match;
/// recognition takes the nearest neighbor across all of them. The voice analogue of face's <c>Person</c>.
/// </summary>
public class Speaker
{
    /// <summary>Document id (auto-generated GUID when left empty).</summary>
    public string Id { get; set; } = "";

    /// <summary>The name given to this voice at enrollment. Multiple documents share the same name.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The voiceprint produced by <see cref="ISpeakerEmbedder"/> — L2-normalized so cosine distance is
    /// meaningful. This is the property mapped via <c>MapVectorProperty</c> for ANN search.
    /// </summary>
    /// <remarks>
    /// Read at insert time by the vector mapping and written to the sqlite-vec sidecar table, so it's excluded
    /// from the JSON document blob. It comes back empty from queries — recognition reads it from the sidecar
    /// via <c>NearestVectors</c>, never from the document.
    /// </remarks>
    [JsonIgnore]
    public ReadOnlyMemory<float> Embedding { get; set; }

    /// <summary>When this utterance was enrolled (UTC).</summary>
    public DateTimeOffset EnrolledAt { get; set; }
}
