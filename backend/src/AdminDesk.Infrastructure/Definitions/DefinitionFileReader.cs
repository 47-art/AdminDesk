using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AdminDesk.Domain.Definitions;
using AdminDesk.SharedKernel.Constants;
using Microsoft.Extensions.Configuration;

namespace AdminDesk.Infrastructure.Definitions;

public sealed record LoadedDefinition(string File, ModuleDefinition Definition, string Json, string ContentHash);

public sealed record DefinitionReadResult(IReadOnlyList<LoadedDefinition> Definitions, IReadOnlyList<string> Problems);

// Reads the embedded definition files and, when configured, the files of an override
// folder. A file in the override folder replaces the embedded file of the same name;
// files with other names are added to the set.
public sealed class DefinitionFileReader
{
    private const string ResourcePrefix = "definitions.";

    private readonly IConfiguration _configuration;

    public DefinitionFileReader(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public DefinitionReadResult Read()
    {
        var files = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        var assembly = typeof(DefinitionFileReader).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) ||
                !resource.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            files[resource[ResourcePrefix.Length..]] = buffer.ToArray();
        }

        var overrideDirectory = _configuration[ConfigKeys.DefinitionsOverrideDirectory];
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            if (!Directory.Exists(overrideDirectory))
            {
                return new DefinitionReadResult(
                    Array.Empty<LoadedDefinition>(),
                    new[] { $"Definition folder '{overrideDirectory}' does not exist" });
            }
            foreach (var path in Directory.GetFiles(overrideDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                files[Path.GetFileName(path)] = File.ReadAllBytes(path);
            }
        }

        var loaded = new List<LoadedDefinition>();
        var problems = new List<string>();
        foreach (var (name, bytes) in files)
        {
            var json = Decode(bytes);
            var shape = new List<string>();
            try
            {
                using var document = JsonDocument.Parse(json);
                ShapeChecker.Check(document.RootElement, typeof(ModuleDefinition), "$", name, shape);
            }
            catch (JsonException ex)
            {
                shape.Add($"Definition '{name}': not valid JSON ({ex.Message})");
            }

            if (shape.Count > 0)
            {
                problems.AddRange(shape);
                continue;
            }

            try
            {
                loaded.Add(new LoadedDefinition(name, DefinitionJson.Parse(json), json, DefinitionHash.Of(bytes)));
            }
            catch (JsonException ex)
            {
                problems.Add($"Definition '{name}': {ex.Message}");
            }
        }

        return new DefinitionReadResult(loaded, problems);
    }

    private static string Decode(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }
}

// Walks a parsed document against the model types so that every unknown property and
// every unknown enum value is reported in one pass, each with its place in the file.
internal static class ShapeChecker
{
    public static void Check(JsonElement element, Type type, string path, string file, List<string> problems)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(JsonElement) || element.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (type.IsEnum)
        {
            CheckEnum(element, type, path, file, problems);
            return;
        }

        if (type != typeof(string) && TryGetElementType(type, out var elementType))
        {
            if (element.ValueKind != JsonValueKind.Array)
            {
                problems.Add($"Definition '{file}': {path} must be a list");
                return;
            }
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                Check(item, elementType, $"{path}[{index++}]", file, problems);
            }
            return;
        }

        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
        {
            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"Definition '{file}': {path} must be an object");
            return;
        }

        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name), p => p, StringComparer.Ordinal);

        foreach (var member in element.EnumerateObject())
        {
            if (!properties.TryGetValue(member.Name, out var property))
            {
                problems.Add(
                    $"Definition '{file}': unknown property '{member.Name}' at {path} (known: {string.Join(", ", properties.Keys)})");
                continue;
            }
            Check(member.Value, property.PropertyType, $"{path}.{member.Name}", file, problems);
        }
    }

    private static void CheckEnum(JsonElement element, Type type, string path, string file, List<string> problems)
    {
        var names = Enum.GetNames(type).Select(n => JsonNamingPolicy.CamelCase.ConvertName(n)).ToList();
        if (element.ValueKind != JsonValueKind.String || !names.Contains(element.GetString()!, StringComparer.OrdinalIgnoreCase))
        {
            problems.Add(
                $"Definition '{file}': unknown value {element.GetRawText()} at {path} (allowed: {string.Join(", ", names)})");
        }
    }

    private static bool TryGetElementType(Type type, out Type elementType)
    {
        elementType = typeof(object);
        if (!typeof(IEnumerable).IsAssignableFrom(type) || !type.IsGenericType)
        {
            return false;
        }
        elementType = type.GetGenericArguments()[0];
        return true;
    }
}
