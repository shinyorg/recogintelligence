using System.Text.Json.Serialization;

namespace Shiny.FaceIntelligence;

/// <summary>
/// An enrolled face. One <see cref="Person"/> document = one captured shot of someone, holding the
/// L2-normalized face embedding used for vector search. Enroll several shots per person for a robust
/// match (varied angles/lighting); recognition takes the nearest neighbor across all of them.
/// </summary>
public class Person
{
    /// <summary>Document id (auto-generated GUID when left empty).</summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// The identity this shot belongs to. Every document with the same value is the same person, and this is
    /// what <c>Recognize</c> returns and <c>Forget</c> deletes by. It is an opaque key chosen by the caller —
    /// a user id, an employee number, a GUID. The library never interprets it and stores no display name of
    /// its own; if you pass a person's name here, that name <i>is</i> the identity.
    /// </summary>
    public string PersonIdentifier { get; set; } = "";

    /// <summary>
    /// The face embedding produced by <see cref="IFaceEmbedder"/> — L2-normalized so cosine distance
    /// is meaningful. This is the property mapped via <c>MapVectorProperty</c> for ANN search.
    /// </summary>
    /// <remarks>
    /// Read at insert time by the vector mapping and written to the sqlite-vec sidecar table, so it's
    /// excluded from the JSON document blob. It comes back empty from queries — recognition reads it
    /// from the sidecar via <c>NearestVectors</c>, never from the document.
    /// </remarks>
    [JsonIgnore]
    public ReadOnlyMemory<float> Embedding { get; set; }

    /// <summary>A small JPEG thumbnail of the cropped face, for display in the people list.</summary>
    public byte[]? Thumbnail { get; set; }

    /// <summary>When this shot was enrolled (UTC).</summary>
    public DateTimeOffset EnrolledAt { get; set; }
}
