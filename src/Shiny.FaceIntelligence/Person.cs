using System.Text.Json.Serialization;

namespace Shiny.FaceIntelligence;

/// <summary>
/// An enrolled face. One <see cref="Person"/> document = one captured shot of someone, holding the
/// L2-normalized face embedding used for vector search. Enroll several shots per name for a robust
/// match (varied angles/lighting); recognition takes the nearest neighbor across all of them.
/// </summary>
public class Person
{
    /// <summary>Document id (auto-generated GUID when left empty).</summary>
    public string Id { get; set; } = "";

    /// <summary>The name given to this face at enrollment. Multiple documents share the same name.</summary>
    public string Name { get; set; } = "";

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
