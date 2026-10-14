using System.Diagnostics;
using System.IO;
using BlueyDesktop.Core.Settings;

namespace BlueyDesktop.App;

/// <summary>
/// The settings loaded once at startup (appsettings.json, then appsettings.Local.json, then the
/// BLUEY_OPENAI_API_KEY environment variable). Read <see cref="Current"/> from anywhere; check
/// <see cref="HasOpenAiKey"/> before using the key so a missing one shows as a state, not a crash.
/// </summary>
internal static class AppSettings
{
    public static BlueySettings Current { get; private set; } = new();

    /// <summary>Why the settings files could not be read, or null when they loaded fine.</summary>
    public static string? LoadError { get; private set; }

    public static bool HasOpenAiKey => Current.OpenAi.HasApiKey;

    /// <summary>Call once, before anything reads <see cref="Current"/>.</summary>
    public static void Load(string directory)
    {
        try
        {
            Current = SettingsLoader.Load(directory);
            LoadError = null;
        }
        catch (InvalidDataException ex)
        {
            // A typo in a settings file must not stop Bluey starting; keep the defaults and say why.
            Current = new BlueySettings();
            LoadError = ex.Message;
            Trace.TraceError($"Bluey settings could not be read: {ex.Message}");
        }

        if (!HasOpenAiKey)
        {
            Trace.TraceWarning(
                $"No OpenAI key configured. Set OpenAi:ApiKey in {SettingsLoader.LocalFileName} or the {SettingsLoader.ApiKeyEnvironmentVariable} environment variable.");
        }
    }
}
