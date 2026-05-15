using System.Diagnostics;
using System.IO.Pipes;
using Shared;

namespace Master;

/// <summary>
/// Master-side handler for a single connected Agent.
/// Owns one NamedPipeServerStream instance, sends the target number,
/// and reads guess messages back until the agent reports a correct guess
/// or the conversation ends.
/// </summary>
public sealed class AgentSession
{
    public string Name { get; }
    public TimeSpan? Elapsed { get; private set; }
    public int? CorrectGuess { get; private set; }
    public bool ReportedSuccess => CorrectGuess.HasValue;

    private readonly NamedPipeServerStream _server;
    private readonly int _target;

    public AgentSession(string name, NamedPipeServerStream server, int target)
    {
        Name = name;
        _server = server;
        _target = target;
    }

    /// <summary>
    /// Waits for this agent to connect, sends the target, then reads guesses
    /// until DONE is received or the pipe closes. The first correct guess
    /// is reported back via the callback so the Master can declare a winner immediately.
    /// </summary>
    /// <param name="onCorrectGuess">Invoked the moment this agent reports a correct guess. Thread-safe in the Master.</param>
    public async Task RunAsync(Func<AgentSession, Task> onCorrectGuess, CancellationToken ct)
    {
        await _server.WaitForConnectionAsync(ct);

        // We use a StreamReader/StreamWriter on top of the duplex pipe.
        // AutoFlush so each WriteLineAsync hits the pipe immediately.
        using var reader = new StreamReader(_server, leaveOpen: true);
        using var writer = new StreamWriter(_server, leaveOpen: true) { AutoFlush = true };

        // Send the target number to the agent and start the clock.
        await writer.WriteLineAsync($"{Protocol.TargetTag}|{_target}");
        var stopwatch = Stopwatch.StartNew();

        while (!ct.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync(ct);
            if (line is null) break; // pipe closed by the other end

            var parts = line.Split('|');
            if (parts.Length == 0) continue;

            switch (parts[0])
            {
                case Protocol.GuessTag when parts.Length == 3 && int.TryParse(parts[2], out int guess):
                    // parts[1] is the agent's self-reported name; we trust our own Name field.
                    if (guess == _target)
                    {
                        stopwatch.Stop();
                        Elapsed = stopwatch.Elapsed;
                        CorrectGuess = guess;
                        await onCorrectGuess(this);
                        return;
                    }
                    break;

                case Protocol.DoneTag:
                    // Agent gave up or shutting down without success.
                    stopwatch.Stop();
                    return;
            }
        }
    }
}
