using System.Diagnostics;

namespace SeqDoc.AcceptanceTests.ProcessOwnershipStub;

/// <summary>
/// Deterministic, ordinary managed test-child for GH-106/I100-A. No P/Invoke, no
/// <c>AllowUnsafeBlocks</c> — every scenario needed by <c>ProcessOwnershipTests.cs</c> is driven purely
/// by command-line arguments and ordinary <see cref="Process"/>/<see cref="Console"/> use.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: <command> [args...]");
            return 64;
        }

        try
        {
            switch (args[0])
            {
                case "echo":
                    return RunEcho(args);
                case "echo-args":
                    return RunEchoArgs(args);
                case "bulk":
                    return RunBulk(args);
                case "slow-bulk":
                    return RunSlowBulk(args);
                case "sleep":
                    return RunSleep(args);
                case "sleep-with-marker":
                    return RunSleepWithMarker(args);
                case "spawn-grandchild":
                    return RunSpawnGrandchild(args);
                case "print-env":
                    return RunPrintEnv(args);
                case "exitcode":
                    return int.Parse(args[1]);
                default:
                    Console.Error.WriteLine($"unknown command: {args[0]}");
                    return 64;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"stub failure: {ex}");
            return 70;
        }
    }

    // echo <stdoutMarker> <stderrMarker>
    private static int RunEcho(string[] args)
    {
        Console.Out.Write(args[1]);
        Console.Out.Flush();
        Console.Error.Write(args[2]);
        Console.Error.Flush();
        return 0;
    }

    // echo-args <arg1> <arg2> ... — proves the OS-level argv the child actually received, one per line,
    // index-prefixed so an empty argument is still visibly a distinct line.
    private static int RunEchoArgs(string[] args)
    {
        for (int i = 1; i < args.Length; i++)
        {
            Console.Out.WriteLine($"{i - 1}:{args[i]}");
        }

        Console.Out.Flush();
        return 0;
    }

    // bulk <stdoutLines> <stderrLines> — deliberately large enough (tens of thousands of lines, each
    // stream several hundred KB minimum) that a naive "read stdout to EOF, then read stderr" caller would
    // deadlock against the child blocking on a full OS pipe buffer it cannot drain.
    private static int RunBulk(string[] args)
    {
        int stdoutLines = int.Parse(args[1]);
        int stderrLines = int.Parse(args[2]);
        int max = Math.Max(stdoutLines, stderrLines);
        const string Filler = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

        for (int i = 0; i < max; i++)
        {
            if (i < stdoutLines)
            {
                Console.Out.WriteLine($"OUT{i:D6}{Filler}");
            }

            if (i < stderrLines)
            {
                Console.Error.WriteLine($"ERR{i:D6}{Filler}");
            }
        }

        Console.Out.Flush();
        Console.Error.Flush();
        return 0;
    }

    // slow-bulk <stdoutLines> <delayMillisecondsPerLine> — deliberately paced so the total run time
    // exceeds any short drain bound a test supplies, proving DrainIncomplete truncation rather than
    // relying on an unpredictable race against the OS pipe buffer size.
    private static int RunSlowBulk(string[] args)
    {
        int stdoutLines = int.Parse(args[1]);
        int delayMs = int.Parse(args[2]);
        for (int i = 0; i < stdoutLines; i++)
        {
            Console.Out.WriteLine($"SLOW{i:D6}");
            Console.Out.Flush();
            Thread.Sleep(delayMs);
        }

        Console.Out.WriteLine("SLOW-COMPLETE");
        Console.Out.Flush();
        return 0;
    }

    // print-env <variableName> — prints the exact value the child process observes (or the literal
    // "<unset>" marker if absent), proving the deterministic explicit environment block reached the
    // child rather than the ambient/inherited environment.
    private static int RunPrintEnv(string[] args)
    {
        string? value = Environment.GetEnvironmentVariable(args[1]);
        Console.Out.Write(value ?? "<unset>");
        Console.Out.Flush();
        return 0;
    }

    // sleep <milliseconds>
    private static int RunSleep(string[] args)
    {
        Thread.Sleep(int.Parse(args[1]));
        return 0;
    }

    // sleep-with-marker <markerPath> <milliseconds> — writes <markerPath> immediately (proof of having
    // started/being alive under containment), sleeps, then writes <markerPath>.completed only if it is
    // allowed to run to natural completion (never true in the forced-termination scenario).
    private static int RunSleepWithMarker(string[] args)
    {
        string markerPath = args[1];
        int milliseconds = int.Parse(args[2]);
        File.WriteAllText(markerPath, Environment.ProcessId.ToString());
        Thread.Sleep(milliseconds);
        File.WriteAllText(markerPath + ".completed", Environment.ProcessId.ToString());
        return 0;
    }

    // spawn-grandchild <markerPath> <milliseconds> — launches an ordinary (non-job-aware)
    // System.Diagnostics.Process running this same stub in sleep-with-marker mode, then exits
    // immediately without waiting. The grandchild is never explicitly assigned to any job by this
    // process; it inherits containment only because Windows places it in the same job as its creator by
    // default (breakaway denied by the primitive under test).
    private static int RunSpawnGrandchild(string[] args)
    {
        string markerPath = args[1];
        string milliseconds = args[2];
        string selfPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Environment.ProcessPath is unavailable.");

        var startInfo = new ProcessStartInfo(selfPath)
        {
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("sleep-with-marker");
        startInfo.ArgumentList.Add(markerPath);
        startInfo.ArgumentList.Add(milliseconds);

        using var grandchild = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start grandchild.");

        Console.Out.WriteLine($"parent-exited grandchild-pid={grandchild.Id}");
        Console.Out.Flush();
        return 0;
    }
}
