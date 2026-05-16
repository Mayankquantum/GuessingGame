using Shared;

namespace Agent;

internal static class Program
{
    /// <summary>
    /// Usage: Agent &lt;name&gt; [coreIndex]
    ///
    /// <paramref name="args"/>[0] = agent name (required)
    /// <paramref name="args"/>[1] = suggested logical core to pin to (optional)
    /// </summary>
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine("Usage: Agent <name> [coreIndex]");
            return 1;
        }

        string name = args[0];

        if (args.Length >= 2 && int.TryParse(args[1], out int coreIndex))
            CpuPinning.PinCurrentProcessToCore(coreIndex);

        try
        {
            var guesser = new Guesser(name);
            await guesser.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Agent {name}] Fatal: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 2;
        }
    }
}
