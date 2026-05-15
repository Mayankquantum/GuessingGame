using System.Diagnostics;
using System.IO.Pipes;
using Shared;

namespace Agent;

/// <summary>
/// Agent entry point.
/// Args: &lt;name&gt; [coreIndex]
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: Agent <name> [coreIndex]");
            return 1;
        }

        string name = args[0];
        int coreIndex = args.Length >= 2 && int.TryParse(args[1], out int parsed) ? parsed : -1;

        Console.WriteLine($"[Agent {name}] Starting (PID {Environment.ProcessId}).");

        if (coreIndex >= 0)
            CpuPinning.TryPinCurrentProcessToCore(coreIndex);

        // Connect to the Master's named pipe.
        using var client = new NamedPipeClientStream(
            ".",
            Protocol.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        Console.WriteLine($"[Agent {name}] Connecting to pipe '{Protocol.PipeName}'...");
        await client.ConnectAsync(timeout: 10_000);
        Console.WriteLine($"[Agent {name}] Connected.");

        using var reader = new StreamReader(client, leaveOpen: true);
        using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };

        // Wait for the target message.
        string? line = await reader.ReadLineAsync();
        if (line is null || !line.StartsWith(Protocol.TargetTag + "|"))
        {
            Console.Error.WriteLine($"[Agent {name}] Expected TARGET, got: {line}");
            return 2;
        }

        if (!int.TryParse(line.AsSpan(Protocol.TargetTag.Length + 1), out int target))
        {
            Console.Error.WriteLine($"[Agent {name}] Could not parse target from: {line}");
            return 3;
        }

        Console.WriteLine($"[Agent {name}] Target received: {target}. Guessing...");

        // Seed mixes time, PID, and name hash so simultaneous agents diverge immediately.
        int seed = HashCode.Combine(Environment.TickCount, Environment.ProcessId, name);
        var guesser = new Guesser(seed);

        var sw = Stopwatch.StartNew();
        int attempts = guesser.GuessUntil(target);
        sw.Stop();

        // Phrase exactly as required by the assignment.
        string report = $"Agent {name} guessed the number {target}.";
        Console.WriteLine($"[Agent {name}] {report} (attempts: {attempts}, time: {sw.Elapsed.TotalMilliseconds:F2} ms)");

        // Wire format: GUESS|<name>|<value>
        await writer.WriteLineAsync($"{Protocol.GuessTag}|{name}|{target}");

        // Polite shutdown signal.
        await writer.WriteLineAsync($"{Protocol.DoneTag}|{name}");

        return 0;
    }
}
