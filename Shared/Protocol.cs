namespace Shared;

/// <summary>
/// Wire protocol used by Master and Agent over the named pipe.
/// All messages are newline-terminated UTF-8 text, fields separated by '|'.
///
///   Agent  -> Master: READY|&lt;name&gt;       (handshake: connected and ready)
///   Master -> Agent : TARGET|&lt;number&gt;
///   Agent  -> Master: GUESS|&lt;name&gt;|&lt;number&gt;|&lt;attempts&gt;
///   Agent  -> Master: DONE|&lt;name&gt;        (optional, on shutdown)
/// </summary>
public static class Protocol
{
    public const string PipeName = "GuessingGamePipe";

    public const string ReadyTag  = "READY";
    public const string TargetTag = "TARGET";
    public const string GuessTag  = "GUESS";
    public const string DoneTag   = "DONE";

    public const int MinNumber = 1;
    public const int MaxNumber = 1000;
}
