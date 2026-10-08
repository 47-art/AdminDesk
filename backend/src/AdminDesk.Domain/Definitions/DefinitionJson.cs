using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdminDesk.Domain.Definitions;

// The one set of parsing rules for definition text: unknown properties and unknown or numeric
// enum values are errors, names are camelCase.
public static class DefinitionJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    public static ModuleDefinition Parse(string json) =>
        JsonSerializer.Deserialize<ModuleDefinition>(json, Options)
        ?? throw new JsonException("The definition is empty.");
}

// The content hash stored with every definition version.
public static class DefinitionHash
{
    public static string Of(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
