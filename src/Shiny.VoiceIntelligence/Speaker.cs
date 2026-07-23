using System.Text.Json.Serialization;

namespace Shiny.VoiceIntelligence;

/// <summary>
/// An enrolled voice. One <see cref="Speaker"/> document = one captured utterance of someone, holding the
/// L2-normalized voiceprint used for vector search. Enroll several utterances per person for a robust match;
/// recognition takes the nearest neighbor across all of them. The voice analogue of face's <c>Person</c>.
/// </summary>
public class Speaker
{
    /// <summary>Document id (auto-generated GUID when left empty).</summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// The identity this utterance belongs to. Every document with the same value is the same person, and this
    /// is what <c>Recognize</c> returns and <c>Forget</c> deletes by. It is an opaque key chosen by the caller
    /// — a user id, an employee number, a GUID. The library never interprets it and stores no display name of
    /// its own; if you pass a person's name here, that name <i>is</i> the identity.
    /// </summary>
    public string PersonIdentifier { get; set; } = "";

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
