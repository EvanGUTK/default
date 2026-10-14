namespace BlueyDesktop.Core.Settings;

/// <summary>Everything configurable through appsettings.json (and its local override).</summary>
public sealed class BlueySettings
{
    public OpenAiSettings OpenAi { get; init; } = new();
}

public sealed class OpenAiSettings
{
    /// <summary>The OpenAI key. Leave it empty in appsettings.json; set it in appsettings.Local.json.</summary>
    public string ApiKey { get; init; } = "";

    /// <summary>Realtime model id. Carried over from the Mac README and not yet checked against a live call.</summary>
    public string RealtimeModel { get; init; } = "gpt-realtime-2.1";

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    // Settings get logged and shown in debuggers; never print the key.
    public override string ToString() => $"OpenAiSettings {{ ApiKey = {(HasApiKey ? "<set>" : "<empty>")}, RealtimeModel = {RealtimeModel} }}";
}
