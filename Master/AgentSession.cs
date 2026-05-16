using System.Diagnostics;
using System.IO.Pipes;
using Shared;

namespace Master;

/// <summary>
/// Master-side handler for one connected Agent. The lifecycle is
/// split into three phases so the Master can orchestrate a fair
/// parallel race across N agents:
///
///   Phase 1: WaitForConnectionAsync  - block until this agent connects.
///   Phase 2: SendTargetAsync         - push the target and start the
///                                      session's stopwatch.
///   Phase 3: ReadResultAsync         - read until this agent reports
///                                      its correct guess (or the pipe
///                                      closes / cancellation fires).
///
/// The Master awaits ALL Phase-1 tasks before invoking Phase 2 on every
/// session via Task.WhenAll. This guarantees no agent gets a head-start
/// caused by uneven process / .NET cold-startup.
/// </summary>
public sealed class AgentSession : IDisposable, IAsyncDisposable
{
    public string Name { get; }
    public TimeSpan? Elapsed { get; private set; }
    public int? CorrectGuess { get; private set; }
    public int? Attempts { get; private set; }
    public bool ReportedSuccess => CorrectGuess.HasValue;

    private readonly NamedPipeServerStream _server;
    private readonly int _target;

    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Stopwatch? _stopwatch;

    public AgentSession(string name, NamedPipeServerStream server, int target)
    {
        Name   = name;
        _server = server;
        _target = target;
    }

    public async Task WaitForConnectionAsync(CancellationToken ct)
    {
        await _server.WaitForConnectionAsync(ct).ConfigureAwait(false);
        _reader = new StreamReader(_server, leaveOpen: true);
        _writer = new StreamWriter(_server, leaveOpen: true) { AutoFlush = true };
    }

    public async Task SendTargetAsync()
    {
        if (_writer is null)
            throw new InvalidOperationException("WaitForConnectionAsync must be awaited first.");

        // Start the per-session stopwatch the moment we hand off the target
        // to this agent, so Elapsed measures only its racing time.
        _stopwatch = Stopwatch.StartNew();
        await _writer.WriteLineAsync($"{Protocol.TargetTag}|{_target}").ConfigureAwait(false);
    }

    public async Task ReadResultAsync(
        Func<AgentSession, Task> onCorrectGuess,
        CancellationToken ct)
    {
        if (_reader is null)
            throw new InvalidOperationException("WaitForConnectionAsync must be awaited first.");

        while (!ct.IsCancellationRequested)
        {
            string? line = await _reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break; // peer closed the pipe

            var parts = line.Split('|');
            if (parts.Length == 0) continue;

            switch (parts[0])
            {
                // GUESS|<name>|<number>|<attempts>
                case Protocol.GuessTag
                    when parts.Length >= 3
                      && int.TryParse(parts[2], out int guess):
                {
                    if (guess != _target) break;

                    _stopwatch!.Stop();
                    Elapsed = _stopwatch.Elapsed;
                    CorrectGuess = guess;
                    if (parts.Length >= 4 && int.TryParse(parts[3], out int att))
                        Attempts = att;

                    await onCorrectGuess(this).ConfigureAwait(false);
                    return;
                }

                case Protocol.DoneTag:
                    _stopwatch?.Stop();
                    return;
            }
        }
    }

    public void Dispose()
    {
        _reader?.Dispose();
        _writer?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_writer is not null)  await _writer.DisposeAsync().ConfigureAwait(false);
        _reader?.Dispose();
    }
}
