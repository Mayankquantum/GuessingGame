using System.Diagnostics;
using System.IO.Pipes;
using Shared;

namespace Master;

/// <summary>
/// Master entry point.
///
/// Usage:
///   Master                          spawns "First" and "Second" (matches the assignment example)
///   Master 5                        spawns 5 agents named "1".."5", each pinned to a different core
///   Master First Second Third       spawns named agents from the explicit list
///
/// Fair race protocol:
///   1. Create N server-side pipe instances on the same pipe name.
///   2. Queue WaitForConnectionAsync on every session.
///   3. Spawn N Agent processes, passing &lt;name&gt; and a suggested core index.
///   4. Await Task.WhenAll(connectionTasks)   <-- barrier
///   5. Send the target to every agent via Task.WhenAll(SendTargetAsync)
///   6. Read every agent's response in parallel; first correct GUIDs the winner.
///   7. Wait for ALL agents to finish so every session has Elapsed + Attempts.
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        List<string> agentNames = ParseAgentNames(args);

        int target = Random.Shared.Next(Protocol.MinNumber, Protocol.MaxNumber + 1);
        DateTimeOffset startedAt = DateTimeOffset.Now;

        PrintHeader(agentNames, target, startedAt);

        string agentExe = LocateAgentExecutable();
        Console.WriteLine($"[Master] Agent executable: {agentExe}");
        Console.WriteLine();

        int maxInstances = agentNames.Count;
        var serverStreams = new List<NamedPipeServerStream>(agentNames.Count);
        var sessions      = new List<AgentSession>(agentNames.Count);

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

            using var cts = new CancellationTokenSource();

            // Winner detection. Interlocked + lock so that the FIRST handler
            // to receive a correct message wins, deterministically, even when
            // several handlers complete on different ThreadPool threads in
            // overlapping nanoseconds.
            object winnerLock = new();
            AgentSession? winner = null;

            Task OnCorrectGuess(AgentSession s)
            {
                lock (winnerLock)
                {
                    if (winner is null)
                    {
                        winner = s;
                        Console.WriteLine($"[Master] *** WINNER: Agent {s.Name} *** (first correct GUESS received)");
                    }
                }
                return Task.CompletedTask;
            }

            // ---- Phase 1: queue WaitForConnectionAsync BEFORE spawning ----
            Console.WriteLine($"[Master] Phase 1: awaiting connections from {agentNames.Count} agents...");
            var connectionTasks = sessions
                .Select(s => s.WaitForConnectionAsync(cts.Token))
                .ToList();

            // ---- Phase 2: spawn agent processes ----
            var processes = SpawnAgents(agentExe, agentNames);

            // ---- Phase 3: barrier ----
            await Task.WhenAll(connectionTasks);
            var raceStartTimer = Stopwatch.StartNew();
            Console.WriteLine($"[Master] Phase 2: all agents connected. Releasing target {target} simultaneously.");

            // ---- Phase 4: fair start - send target to all in parallel ----
            await Task.WhenAll(sessions.Select(s => s.SendTargetAsync()));

            // ---- Phase 5: read all guess streams concurrently ----
            var readTasks = sessions
                .Select(s => s.ReadResultAsync(OnCorrectGuess, cts.Token))
                .ToArray();
            await Task.WhenAll(readTasks);
            raceStartTimer.Stop();

            // ---- Phase 6: report ----
            PrintResults(sessions, winner, target, raceStartTimer.Elapsed);

            // ---- Cleanup ----
            foreach (var p in processes)
            {
                if (p.HasExited) continue;
                try { p.WaitForExit(2000); } catch { /* ignore */ }
                if (!p.HasExited) { try { p.Kill(entireProcessTree: true); } catch { } }
            }

            return 0;
        }
        finally
        {
            foreach (var s in sessions)       s.Dispose();
            foreach (var s in serverStreams)  s.Dispose();
        }
    }

    private static List<Process> SpawnAgents(string agentExe, List<string> agentNames)
    {
        var processes = new List<Process>(agentNames.Count);
        for (int i = 0; i < agentNames.Count; i++)
        {
            var psi = new ProcessStartInfo
            {
                FileName        = agentExe,
                UseShellExecute = false,
                CreateNoWindow  = false,
            };
            psi.ArgumentList.Add(agentNames[i]);
            psi.ArgumentList.Add(i.ToString()); // suggested core index

            var p = Process.Start(psi)
                    ?? throw new InvalidOperationException($"Failed to start agent {agentNames[i]}.");
            processes.Add(p);
        }
        return processes;
    }

    private static void PrintHeader(List<string> agentNames, int target, DateTimeOffset startedAt)
    {
        Console.WriteLine("===============================================================");
        Console.WriteLine($"   Master / Agent guessing race");
        Console.WriteLine($"   Started   : {startedAt:yyyy-MM-dd HH:mm:ss zzz}");
        Console.WriteLine($"   Agents    : {agentNames.Count}  ({string.Join(", ", agentNames)})");
        Console.WriteLine($"   Cores     : {Environment.ProcessorCount} logical");
        Console.WriteLine($"   Range     : [{Protocol.MinNumber} .. {Protocol.MaxNumber}]");
        Console.WriteLine($"   Target    : {target}");
        Console.WriteLine("===============================================================");
    }

    private static void PrintResults(
        List<AgentSession> sessions,
        AgentSession? winner,
        int target,
        TimeSpan totalRace)
    {
        Console.WriteLine();
        Console.WriteLine("================== FINAL RESULTS (fastest first) ==================");
        Console.WriteLine($"  Target               : {target}");
        Console.WriteLine($"  Total race wall-time : {totalRace.TotalMilliseconds:F2} ms");
        Console.WriteLine($"  Winner               : Agent {(winner?.Name ?? "(none)")}");
        Console.WriteLine();
        Console.WriteLine("  Name           Attempts        Elapsed (ms)     Status");
        Console.WriteLine("  -------------  -------------   --------------   ---------");

        foreach (var s in sessions.OrderBy(x => x.Elapsed ?? TimeSpan.MaxValue))
        {
            string attempts = s.Attempts?.ToString() ?? "-";
            string elapsed  = s.Elapsed.HasValue
                ? s.Elapsed.Value.TotalMilliseconds.ToString("F2")
                : "-";
            string status   = s.ReportedSuccess ? "OK" : "no result";
            Console.WriteLine($"  {s.Name,-13}  {attempts,-13}   {elapsed,-14}   {status}");
        }
        Console.WriteLine("====================================================================");
    }

    private static List<string> ParseAgentNames(string[] args)
    {
        if (args.Length == 0)
            return new List<string> { "First", "Second" };

        if (args.Length == 1 && int.TryParse(args[0], out int n) && n > 0)
            return Enumerable.Range(1, n).Select(i => i.ToString()).ToList();

        return args.ToList();
    }

    /// <summary>
    /// Resolves the path to Agent.exe (Windows) or Agent (Unix) sitting in
    /// the sibling project's bin/&lt;Config&gt;/&lt;TFM&gt;/ folder.
    /// </summary>
    private static string LocateAgentExecutable()
    {
        string masterDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        // .../GuessingGame/Master/bin/<Config>/<TFM>
        string? tfmDir    = masterDir;
        string? configDir = Path.GetDirectoryName(tfmDir);
        string? binDir    = Path.GetDirectoryName(configDir);
        string? projDir   = Path.GetDirectoryName(binDir);
        string? rootDir   = Path.GetDirectoryName(projDir);

        if (rootDir is null || configDir is null || tfmDir is null)
            throw new InvalidOperationException("Cannot derive Agent path from Master path.");

        string tfm    = Path.GetFileName(tfmDir);
        string config = Path.GetFileName(configDir);
        string exe    = OperatingSystem.IsWindows() ? "Agent.exe" : "Agent";

        string candidate = Path.Combine(rootDir, "Agent", "bin", config, tfm, exe);

        if (!File.Exists(candidate))
            throw new FileNotFoundException(
                $"Agent executable not found. Build the Agent project first. Looked at: {candidate}",
                candidate);

        return candidate;
    }
}
