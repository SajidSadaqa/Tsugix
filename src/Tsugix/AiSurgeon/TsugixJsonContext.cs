using System.Text.Json.Serialization;

namespace Tsugix.AiSurgeon;

/// <summary>
/// JSON serialization context for AOT compatibility.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TsugixConfig))]
[JsonSerializable(typeof(FixSuggestion))]
[JsonSerializable(typeof(FixEdit))]
[JsonSerializable(typeof(LlmProvider))]
public partial class TsugixJsonContext : JsonSerializerContext
{
}
