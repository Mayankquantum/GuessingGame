using System.Diagnostics;
using System.IO.Pipes;
using Shared;

namespace Master;

/// <summary>
/// Master entry point.
/// Usage:
///   Master                          -> spawns 2 agents named "First" and "Second"
///   Master 5                        -> spawns 5 agents named "1".."5", each pinned to a core
///   Master First Second Third       -> spawns named agents from the list
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var agentNames = ParseAgentNames(args);
        Console.WriteLine($"[Master] Starting with {agentNames.Count} agents: {string.Join(", ", agentNames)}");
        Console.WriteLine($"[Master] Machine has {Environment.ProcessorCount} logical cores.");

        // Pick a target the agents will hunt for.
        int target = Random.Shared.Next(Protocol.MinNumber, Protocol.MaxNumber + 1);
        Console.WriteLine($"[Master] Secret number is {target} (range {Protocol.MinNumber}-{Protocol.MaxNumber}).");

        // Locate the Agent executable. We assume it sits in a sibling bin folder under the same configuration.
        string agentExe = LocateAgentExecutable();
        Console.WriteLine($"[Master] Agent executable: {agentExe}");

        // Stand up one server pipe instance per agent BEFORE spawning processes,
        // so the agents always have something to connect to.
        // maxNumberOfServerInstances must accommodate every concurrent agent.
        int maxInstances = Math.Max(agentNames.Count, NamedPipeServerStream.MaxAllowedServerInstances);
        var sessions = new List<AgentSession>(agentNames.Count);
        var serverStreams = new List<NamedPipeServerStream>(agentNames.Count);

        try
        {
            for (int i = 0; i < agentNames.Count; i++)
            {
                var server = new NamedPipeServerStream(
                    Protocol.PipeName,
                    PipeDirection.InOut,
                    maxInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                serverStreams.Add(server);
                sessions.Add(new AgentSession(agentNames[i], server, target));
            }

            // Cancellation token used to stop late agents once a winner is declared.
            using var cts = new CancellationTokenSource();

            // Synchronisation: only the FIRST correct guess wins.
            var winnerLock = new object();
            AgentSession? winner = null;

            Task OnCorrectGuess(AgentSession s)
            {
                lock (winnerLock)
                {
                    if (winner is null)
                    {
                        winner = s;
                        Console.WriteLine($"[Master] First correct report received from {s.Name}. Cancelling remaining sessions.");
                        cts.Cancel();
                    }
                }
                return Task.CompletedTask;
            }

            // Kick off the session loops BEFORE starting the processes,
            // so WaitForConnectionAsync is already pending.
            var sessionTasks = sessions
                .Select(s => Task.Run(() => s.RunAsync(OnCorrectGuess, cts.Token), cts.Token))
                .ToList();

            // Spawn the agent processes. Pass: name, [optional core index].
            var processes = new List<Process>(agentNames.Count);
            for (int i = 0; i < agentNames.Count; i++)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = agentExe,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                };
                psi.ArgumentList.Add(agentNames[i]);
                psi.ArgumentList.Add(i.ToString()); // suggested core index
                var p = Process.Start(psi)
                        ?? throw new InvalidOperationException($"Failed to start agent {agentNames[i]}.");
                processes.Add(p);
            }

            // Wait for either: a winner is declared, or every session finished without a winner.
            try
            {
                await Task.WhenAll(sessionTasks);
            }
            catch (OperationCanceledException)
            {
                // Expected when we cancel after declaring a winner.
            }

            // Print results.
            Console.WriteLine();
            Console.WriteLine("====== Results ======");
            foreach (var s in sessions)
            {
                if (s.ReportedSuccess)
                    Console.WriteLine($"  {s.Name}: guessed {s.CorrectGuess} in {s.Elapsed!.Value.TotalMilliseconds:F2} ms");
                else
                    Console.WriteLine($"  {s.Name}: did not report a correct guess");
            }

            if (winner is not null)
                Console.WriteLine($"Winner: Agent {winner.Name}");
            else
                Console.WriteLine("No winner.");

            // Make sure agent processes exit cleanly.
            foreach (var p in processes)
            {
                if (!p.HasExited)
                {
                    try { p.WaitForExit(2000); } catch { /* ignore */ }
                    if (!p.HasExited) p.Kill(entireProcessTree: true);
                }
            }

            return 0;
        }
        finally
        {
            foreach (var s in serverStreams) s.Dispose();
        }
    }

    private static List<string> ParseAgentNames(string[] args)
    {
        // No args: default pair from the assignment example.
        if (args.Length == 0) return new List<string> { "First", "Second" };

        // Single integer: spawn N agents named "1".."N".
        if (args.Length == 1 && int.TryParse(args[0], out int n) && n > 0)
            return Enumerable.Range(1, n).Select(i => i.ToString()).ToList();

        // Otherwise treat all args as explicit names.
        return args.ToList();
    }

    private static string LocateAgentExecutable()
    {
        string masterDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string rootDir = Path.GetFullPath(Path.Combine(masterDir, "..", "..", "..", ".."));
        string framework = Path.GetFileName(masterDir);
        string config    = Path.GetFileName(Path.GetDirectoryName(masterDir));
        string exeName   = OperatingSystem.IsWindows() ? "Agent.exe" : "Agent";
        string candidate = Path.Combine(rootDir, "Agent", "bin", config, framework, exeName);
        if (!File.Exists(candidate))
            throw new FileNotFoundException("Agent executable not found.", candidate);
        return candidate;
    }}
