# Master & Agent — Number Guessing over Named Pipes

OOP assignment: Inter-Process Communication using **Named Pipes**, **async/await**, and **CPU core affinity**.

- **Master** spawns N Agent processes and sends each a target number (1–1000).
- Each **Agent** repeatedly calls `Random.Next` until it guesses the target, then sends a message back through the pipe.
- The Master reports the first agent to succeed as the **Winner** and prints per-agent timings.

## Project layout

```
GuessingGame/
├── GuessingGame.sln
├── Shared/        # Protocol constants, CPU pinning helper
│   ├── Protocol.cs
│   └── CpuPinning.cs
├── Master/        # Master.exe (pipe server, orchestrator)
│   ├── Program.cs
│   └── AgentSession.cs
└── Agent/         # Agent.exe (pipe client, guesser)
    ├── Program.cs
    └── Guesser.cs
```

## Requirements

- .NET 10 SDK
- Windows, macOS, or Linux

> **Note on CPU pinning:** `Process.ProcessorAffinity` is **Windows-only** in .NET. On macOS/Linux the `CpuPinning` helper logs that pinning was skipped — the rest of the program runs identically. The assignment treats core assignment as optional, so this is compliant.

## Build

```bash
cd GuessingGame
dotnet build
```

This produces:
- `Master/bin/Debug/net10.0/Master(.exe)`
- `Agent/bin/Debug/net10.0/Agent(.exe)`

The Master locates the Agent executable automatically, assuming both projects were built into the standard `bin/<Config>/net10.0` paths.

## Run

```bash
# Default: two agents named First and Second
dotnet run --project Master

# Spawn N agents named "1".."N", each pinned to core (i-1) on Windows
dotnet run --project Master -- 5

# Explicit names
dotnet run --project Master -- Alpha Bravo Charlie
```

Or run the built executables directly:

```bash
./Master/bin/Debug/net10.0/Master 4
```

## Wire protocol

One line per message, pipe-delimited:

| Direction        | Format                  | Example                |
| ---------------- | ----------------------- | ---------------------- |
| Master → Agent   | `TARGET\|<n>`           | `TARGET\|742`          |
| Agent → Master   | `GUESS\|<name>\|<n>`    | `GUESS\|First\|742`    |
| Agent → Master   | `DONE\|<name>`          | `DONE\|First`          |

The pipe is `PipeDirection.InOut` so each Master↔Agent pair runs over a single duplex connection. The Master opens **one server stream per agent** using the same pipe name plus `maxNumberOfServerInstances`, which is the standard .NET pattern for multi-client named pipes.

## Synchronisation

- Master uses `await` on `WaitForConnectionAsync`, `ReadLineAsync`, and `WriteLineAsync` — no thread blocks while waiting for an agent.
- A `lock` around winner declaration guarantees only the **first** correct report wins, even if two agents finish in the same instant.
- A `CancellationTokenSource` cancels the remaining session loops once a winner exists.
- After printing results, the Master `WaitForExit`s every agent process (with a 2-second timeout, then `Kill` as a safety net).

## Testing report

See `report.txt` for example runs.
