namespace BlueyDesktop.Core.Protocol;

/// <summary>
/// Placeholder for the pairing protocol. The names mirror the Mac/iPhone command set so a shared
/// vocabulary exists; nothing sends these on Windows yet, and the final protocol (and whether
/// Windows needs a phone link at all) is still to be decided.
/// </summary>
public enum Command
{
    Wake,
    Sleep,
    RealtimeToken,
    Transcribe,
    Notes,
    Tool,
    Caption,
    CaptionDone,
    Awake,
    Asleep,
    ToolResult,
    Transcribed,
}

/// <summary>Version of the packet vocabulary. Bump it when an older peer would misread a packet.</summary>
public static class ProtocolInfo
{
    public const int Version = 1;
}
