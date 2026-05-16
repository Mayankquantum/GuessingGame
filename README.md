# Master & Agent — Number Guessing over Named Pipes

OOP coursework, Vilnius University Šiauliai Academy.

A Master process and N Agent processes communicate over a single named pipe.
The Master generates a random target number; the agents race to guess it.
The first correct message back wins. Every agent reports its attempt count
and timing, and the Master pins each agent process to a distinct logical
CPU core (on Windows).

---

## Architecture

```
                    +---------------------------+
                    |        Master.exe         |
                    |  - picks target X in 1..1000
                    |  - spawns N agents        |
                    |  - sets ProcessorAffinity |
                    |  - barrier on connections |
                    |  - releases target X to   |
                    |    all agents at once     |
                    |  - reads results in       |
                    |    parallel               |
                    +-----+--------+------+-----+
                          |        |      |
              named pipe  |        |      |   one server-side
              "GuessingGamePipe"   |      |   instance per agent
                          |        |      |   (PipeDirection.InOut,
                          v        v      v   PipeOptions.Asynchronous)
                    +---------+ +-------+ +-----+
                    |Agent 1  | |Agent 2| |... N|
                    +---------+ +-------+ +-----+
                       \           |         /
                        \    each pinned    /
                         \  to its own core
                          (Windows only)
```

### Wire protocol (line-based, UTF-8)

| Direction         | Message                                    |
|-------------------|--------------------------------------------|
| Master  -> Agent  | `TARGET\|<number>`                          |
| Agent   -> Master | `GUESS\|<name>\|<number>\|<attempts>`        |
| Agent   -> Master | `DONE\|<name>` (optional, on shutdown)      |

### Race sequence (timeline)

```
t0  Master: create N server pipe instances
t1  Master: queue WaitForConnectionAsync on every session
t2  Master: spawn N agent processes
t3  Each agent: connect to its server-side instance
t4  Master: barrier --> Task.WhenAll(connectionTasks)
t5  Master: Task.WhenAll(SendTargetAsync) -- fair start, ~simultaneous
t6  Each agent: receive TARGET, brute-force Random.Next until match
t7  Each agent: send GUESS|name|target|attempts
t8  Master: first GUESS handler to process the line claims the winner
            (Interlocked + lock so this is deterministic across threads)
t9  Master: await Task.WhenAll(readTasks) -- every agent reports a time
t10 Master: print sorted results table + winner
```

The barrier at t4 is critical: it guarantees every agent's stopwatch
starts at essentially the same instant. Without it, whichever process's
.NET runtime cold-starts fastest gets a head start and the "race"
measures startup luck instead of guessing speed.

---

## Project layout

```
GuessingGame/
├── GuessingGame.sln
├── README.md
├── .gitignore
├── Shared/                <- common code referenced by both projects
│   ├── Shared.csproj
│   ├── Protocol.cs        <- pipe name, tags, number range
│   └── CpuPinning.cs      <- ProcessorAffinity wrapper (Windows-only)
├── Master/
│   ├── Master.csproj
│   ├── Program.cs         <- orchestrator: barrier + parallel race
│   └── AgentSession.cs    <- per-agent state machine (Connect/Send/Read)
├── Agent/
│   ├── Agent.csproj
│   ├── Program.cs         <- entry, parses name + core index
│   └── Guesser.cs         <- pipe client + guessing loop
├── Reports/
│   ├── test_report_1.txt  <- 2 agents
│   ├── test_report_2.txt  <- 4 agents
│   └── test_report_3.txt  <- 8 agents (stress)
└── Diagrams/
    ├── sequence.md        <- Mermaid sequence diagram
    └── classes.md         <- Mermaid class diagram
```

---

## Build

Requires .NET 10 SDK.

```
dotnet build GuessingGame.sln -c Debug
```

Produces:
- `Master/bin/Debug/net10.0/Master(.exe)`
- `Agent/bin/Debug/net10.0/Agent(.exe)`

## Run

```
# default: 2 agents, names "First" and "Second"
dotnet Master/bin/Debug/net10.0/Master.dll

# N agents named "1".."N"
dotnet Master/bin/Debug/net10.0/Master.dll 4

# explicit names
dotnet Master/bin/Debug/net10.0/Master.dll Alpha Bravo Charlie
```

On Windows you can run the .exe directly.

---

## Key implementation choices

1. **One pipe name, N server instances.** `NamedPipeServerStream` is created
   N times with the same name and `maxNumberOfServerInstances = N`. Each
   handler task owns one instance and accepts exactly one agent — the
   canonical .NET pattern for many-to-one named-pipe servers.

2. **`async/await` end to end.** `WaitForConnectionAsync`,
   `ReadLineAsync`, `WriteLineAsync` — no thread is parked during a wait.
   Combined with `PipeOptions.Asynchronous`, the ThreadPool stays free.

3. **Connection barrier.** `Task.WhenAll` over every session's
   `WaitForConnectionAsync` runs before any target is sent. This is the
   "synchronization and coordination" piece called out in the task.

4. **Simultaneous release.** `Task.WhenAll(sessions.Select(s => s.SendTargetAsync()))`
   schedules every write at the same moment, so all agents start guessing
   in the same wall-clock window.

5. **Winner detection without a race.** A single `Interlocked.CompareExchange`
   isn't enough because the callback also prints to the console; we use a
   `lock` block that checks-then-sets `winner`. The first handler in
   wins; every other call to the callback becomes a no-op.

6. **CPU affinity.** `Process.ProcessorAffinity = (IntPtr)(1L << core)`.
   On macOS / Linux this throws, so `CpuPinning` checks
   `OperatingSystem.IsWindows()` first and degrades to a notice line.

7. **Full per-agent telemetry.** The protocol now carries `attempts`
   from agent to master, so the final results table shows both
   attempts AND elapsed milliseconds for every agent — not just the
   winner.

---

## Submission checklist (per the task brief)

- [x] Master and Agent are separate console applications
- [x] Master spawns Agent processes via CLI parameters (`Master 4`, etc.)
- [x] All agents connect to the same named pipe (`GuessingGamePipe`)
- [x] Master generates random X in [1, 1000] and sends it to every agent
- [x] Agents use `Random.Next` in a loop until they hit X
- [x] On correct guess agent sends `"Agent <name> guessed the number X."`
      (the GUESS protocol message carries the same information)
- [x] Master uses `async/await` for all pipe I/O
- [x] Master prints `Winner: Agent <name>` on the first correct message
- [x] Master measures and records each agent's time
- [x] Master accepts an N parameter and pins each agent to a CPU core
- [x] Source code split into classes across files
- [x] 3 test report `.txt` files in `Reports/`
- [x] Sequence + class diagrams in `Diagrams/`
- [x] GitHub repo: https://github.com/Mayankquantum/GuessingGame
