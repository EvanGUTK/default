using System.Text.Json;
using System.Text.Json.Nodes;

namespace BlueyDesktop.Core.Settings;

/// <summary>
/// Loads <see cref="BlueySettings"/> in layers, later layers winning:
/// appsettings.json (committed, no secrets), appsettings.Local.json (git-ignored, your key),
/// then the BLUEY_OPENAI_API_KEY environment variable.
/// </summary>
public static class SettingsLoader
{
    public const string BaseFileName = "appsettings.json";
    public const string LocalFileName = "appsettings.Local.json";
    public const string ApiKeyEnvironmentVariable = "BLUEY_OPENAI_API_KEY";

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Reads the files from <paramref name="directory"/> (normally the app's output folder).
    /// A missing file is skipped; a file that is not valid JSON throws <see cref="InvalidDataException"/>
    /// naming the file, so a typo never silently turns into "no key".
    /// </summary>
    public static BlueySettings Load(string directory, Func<string, string?>? getEnvironmentVariable = null)
    {
        var merged = new JsonObject();
        foreach (var name in new[] { BaseFileName, LocalFileName })
        {
            var layer = ReadLayer(Path.Combine(directory, name));
            if (layer is not null) Merge(merged, layer);
        }

        var settings = merged.Deserialize<BlueySettings>(SerializerOptions) ?? new BlueySettings();

        var fromEnvironment = (getEnvironmentVariable ?? Environment.GetEnvironmentVariable)(ApiKeyEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            settings = new BlueySettings { OpenAi = new OpenAiSettings { ApiKey = fromEnvironment.Trim(), RealtimeModel = settings.OpenAi.RealtimeModel } };
        }
        return settings;
    }

    private static JsonObject? ReadLayer(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonNode.Parse(File.ReadAllText(path), documentOptions: DocumentOptions) as JsonObject
                ?? throw new InvalidDataException($"{path}: expected a JSON object at the top level.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path}: {ex.Message}", ex);
        }
    }

    /// <summary>Copies <paramref name="overlay"/> onto <paramref name="target"/>; nested objects merge, anything else is replaced. Keys match ignoring case.</summary>
    private static void Merge(JsonObject target, JsonObject overlay)
    {
        foreach (var (key, value) in overlay.ToList())
        {
            var existingKey = target.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (existingKey is not null && target[existingKey] is JsonObject existing && value is JsonObject incoming)
            {
                Merge(existing, incoming);
                continue;
            }
            if (existingKey is not null) target.Remove(existingKey);
            overlay.Remove(key);
            target[key] = value;
        }
    }
}
