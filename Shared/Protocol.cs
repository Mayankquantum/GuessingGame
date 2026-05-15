namespace Shared;

/// <summary>
/// Constants used by both Master and Agent so the wire protocol stays in one place.
/// </summary>
public static class Protocol
{
    /// <summary>The named pipe identifier. Both ends must agree on this.</summary>
    public const string PipeName = "GuessingGamePipe";

    /// <summary>Inclusive lower bound for the secret number.</summary>
    public const int MinNumber = 1;

    /// <summary>Inclusive upper bound for the secret number.</summary>
    public const int MaxNumber = 1000;

    // --- Message tags. Format on the wire is one line per message: TAG|payload ---

    /// <summary>Master -> Agent. Payload: the target number, e.g. "TARGET|742".</summary>
    public const string TargetTag = "TARGET";

    /// <summary>Agent -> Master. Payload: agent name + guessed value, e.g. "GUESS|First|742".</summary>
    public const string GuessTag = "GUESS";

    /// <summary>Agent -> Master. Sent on shutdown so Master doesn't hang waiting. Payload: agent name.</summary>
    public const string DoneTag = "DONE";
}
