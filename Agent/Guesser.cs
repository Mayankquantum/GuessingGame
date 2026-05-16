using System.Diagnostics;
using System.IO.Pipes;
using Shared;

namespace Agent;

/// <summary>
/// Agent-side client. Connects to the Master's named pipe, receives the
/// target number, and brute-forces uniform random guesses in
/// [MinNumber..MaxNumber] until it hits the target. Reports the result
/// (with the attempt count) back through the same pipe.
/// </summary>
public sealed class Guesser
{
    private const int ConnectTimeoutMs = 15_000;
    private readonly string _name;

    public Guesser(string name)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public async Task RunAsync()
    {
        Console.WriteLine($"[Agent {_name}] Starting (PID {Environment.ProcessId}). Connecting to pipe '{Protocol.PipeName}'...");

        using var pipe = new NamedPipeClientStream(
            serverName: ".",
            pipeName:   Protocol.PipeName,
            direction:  PipeDirection.InOut,
            options:    PipeOptions.Asynchronous);

        await pipe.ConnectAsync(ConnectTimeoutMs).ConfigureAwait(false);

        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

        Console.WriteLine($"[Agent {_name}] Connected. Sending READY handshake...");

        // Application-level handshake: tell the Master we are fully connected
        // and parked on ReadLineAsync, ready to race. The Master will not
        // release the target until EVERY agent has sent this.
        await writer.WriteLineAsync($"{Protocol.ReadyTag}|{_name}").ConfigureAwait(false);

        Console.WriteLine($"[Agent {_name}] READY sent. Awaiting target...");

        // Receive TARGET|<number>
        string? line = await reader.ReadLineAsync().ConfigureAwait(false);
        if (line is null)
        {
            Console.WriteLine($"[Agent {_name}] Pipe closed before target arrived. Exiting.");
            return;
        }

        var parts = line.Split('|');
        if (parts.Length < 2 || parts[0] != Protocol.TargetTag || !int.TryParse(parts[1], out int target))
        {
            Console.WriteLine($"[Agent {_name}] Malformed target message: '{line}'. Exiting.");
            return;
        }

        Console.WriteLine($"[Agent {_name}] Target received: {target}. Guessing in [{Protocol.MinNumber}..{Protocol.MaxNumber}]...");

        // Brute-force loop. A per-process seed avoids identical sequences
        // when several agents start in the same tick.
        int seed = unchecked(
              _name.GetHashCode()
            ^ Environment.ProcessId
            ^ Environment.TickCount);
        var rng = new Random(seed);

        var sw = Stopwatch.StartNew();
        int attempts = 0;
        int guess;
        do
        {
            guess = rng.Next(Protocol.MinNumber, Protocol.MaxNumber + 1);
            attempts++;
        }
        while (guess != target);
        sw.Stop();

        string msg = $"{Protocol.GuessTag}|{_name}|{target}|{attempts}";
        await writer.WriteLineAsync(msg).ConfigureAwait(false);

        Console.WriteLine($"[Agent {_name}] DONE: guessed {target} in {attempts} attempts ({sw.Elapsed.TotalMilliseconds:F2} ms of pure guessing).");
    }
}
