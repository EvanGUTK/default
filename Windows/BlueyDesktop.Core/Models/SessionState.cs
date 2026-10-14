namespace BlueyDesktop.Core.Models;

/// <summary>Where the voice session is. Placeholder: grow it as the Windows app takes shape.</summary>
public enum SessionState
{
    /// <summary>No session. Bluey just follows the mouse.</summary>
    Asleep,
    /// <summary>Getting a token and opening the Realtime connection.</summary>
    Connecting,
    /// <summary>The mic is on and everything said becomes context.</summary>
    Listening,
    /// <summary>A question was asked and he is working on the answer or running a tool.</summary>
    Thinking,
}
