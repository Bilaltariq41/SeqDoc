using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SeqDoc.AcceptanceTests.ProcessOwnershipStub;

/// <summary>
/// Deterministic, ordinary managed test-child for GH-106/I100-A. No <c>AllowUnsafeBlocks</c> — every
/// scenario needed by <c>ProcessOwnershipTests.cs</c> is driven purely by command-line arguments and
/// ordinary <see cref="Process"/>/<see cref="Console"/> use, plus (GH106-R2-F12) one plain, pointer-free
/// P/Invoke signature used only for the <c>report-job-membership</c> observable-receipt command.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        // GH106-R2-F12: as the very first action this process takes — before any other argument
        // handling, console I/O, or logic — report whether the OS already considers this process a job
        // member. This is a genuine, external, production-code-path receipt proving job membership (via
        // AssignProcessToJobObject) was established before this child ever executed any other
        // instruction, replacing/supplementing the prior in-process test-hook-only proof.
        if (args.Length > 0 && args[0] == "report-job-membership")
        {
            return RunReportJobMembership();
        }

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
                case "read-stdin-to-eof":
                    return RunReadStdinToEof();
                case "utf8-boundary":
                    return RunUtf8Boundary();
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

    // report-job-membership — see the dispatch comment in Main above. Queries this process's own
    // membership in any job before doing anything else and prints it as the very first stdout line.
    private static int RunReportJobMembership()
    {
        IsProcessInJob(GetCurrentProcess(), nint.Zero, out bool inJob);
        Console.Out.WriteLine($"IN-JOB:{inJob}");
        Console.Out.Flush();
        return 0;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(nint processHandle, nint jobHandle, [MarshalAs(UnmanagedType.Bool)] out bool result);

    // read-stdin-to-eof — GH106-R2-F6: proves the parent's stdin write handle is closed immediately (not
    // retained-but-unusable), so a child reading stdin to EOF completes instead of hanging.
    private static int RunReadStdinToEof()
    {
        string all = Console.In.ReadToEnd();
        Console.Out.Write($"READ-COMPLETE:{all.Length}");
        Console.Out.Flush();
        return 0;
    }

    // utf8-boundary — GH106-R2-F10: writes exactly 65535 single-byte filler bytes, then a 3-byte UTF-8
    // character (so its first byte lands at offset 65535), then a trailing marker. The caller (see
    // ContainedProcess.PipeBufferSizeOverrideForTests in ProcessOwnershipTests.cs) forces the pipe's
    // buffer large enough to hold this whole payload atomically, so the split is guaranteed, by
    // construction, to land exactly at that offset, straddling DrainPipe's real 64 KiB read boundary.
    private static int RunUtf8Boundary()
    {
        using var stdout = Console.OpenStandardOutput();
        var filler = new byte[65535];
        Array.Fill(filler, (byte)'x');
        stdout.Write(filler, 0, filler.Length);

        byte[] straddling = Encoding.UTF8.GetBytes("€");
        stdout.Write(straddling, 0, straddling.Length);

        byte[] marker = Encoding.UTF8.GetBytes("-MARKER-END");
        stdout.Write(marker, 0, marker.Length);
        stdout.Flush();
        return 0;
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
