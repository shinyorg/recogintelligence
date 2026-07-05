using System.Text.Json.Serialization;

namespace Shiny.VoiceIntelligence;

/// <summary>
/// Source-generated JSON metadata for the document types, so DocumentDb serialization and the LINQ
/// expression visitor are AOT/trim-safe. Do NOT add <c>[JsonSerializerContext]</c> — it's inherited.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Speaker))]
public partial class VoicesJsonContext : JsonSerializerContext;
