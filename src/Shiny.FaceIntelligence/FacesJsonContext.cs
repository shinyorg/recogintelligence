using System.Text.Json.Serialization;

namespace Shiny.FaceIntelligence;

/// <summary>
/// Source-generated JSON metadata for the document types, so DocumentDb serialization and the LINQ
/// expression visitor are AOT/trim-safe. Do NOT add <c>[JsonSerializerContext]</c> — it's inherited.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Person))]
public partial class FacesJsonContext : JsonSerializerContext;
