using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ClassicsStudio.Packaging;

internal static class PackageTool
{
    public static ProcessStartInfo Command(string file, string directory, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(file)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    public static async Task RunAsync(ProcessStartInfo info, CancellationToken token,
        Action<string>? stdout = null, Action<string>? stderr = null, Action? sample = null,
        bool allowWarningExit = false)
    {
        token.ThrowIfCancellationRequested();
        using var process = Process.Start(info) ?? throw new IOException($"Could not start {info.FileName}.");
        // Cancel can run on the UI thread. Signal the monitor there and let it
        // terminate the tool on a worker thread.
        var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => stop.TrySetResult());
        var outputTail = new Queue<string>();
        var errorTail = new Queue<string>();

        async Task Drain(StreamReader reader, Queue<string> tail, Action<string>? observe)
        {
            try
            {
                // Some helpers emit long progress lines with no newline. Bound
                // each line while still draining every byte from both pipes.
                const int maxLine = 4096;
                char[] buffer = new char[maxLine];
                var line = new StringBuilder();
                void FinishLine()
                {
                    if (line.Length == 0) return;
                    string value = line.ToString();
                    line.Clear();
                    if (string.IsNullOrWhiteSpace(value)) return;
                    if (tail.Count == 6) tail.Dequeue();
                    tail.Enqueue(value.Length <= 350 ? value : value[..350]);
                    observe?.Invoke(value);
                }
                int count;
                while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
                {
                    int offset = 0;
                    while (offset < count)
                    {
                        int delimiter = buffer.AsSpan(offset, count - offset).IndexOfAny('\r','\n');
                        int length = delimiter < 0 ? count - offset : delimiter;
                        line.Append(buffer, offset, Math.Min(length, maxLine - line.Length));
                        offset += length;
                        if (delimiter >= 0) { FinishLine(); offset++; }
                    }
                }
                FinishLine();
            }
            catch { stop.TrySetResult(); throw; }
        }

        Task completed = Task.WhenAll(process.WaitForExitAsync(),
            Drain(process.StandardOutput, outputTail, stdout), Drain(process.StandardError, errorTail, stderr));
        var finishedOrStopping = Task.WhenAny(completed, stop.Task);
        try
        {
            if (sample is not null)
                while (await Task.WhenAny(finishedOrStopping, Task.Delay(200)).ConfigureAwait(false) != finishedOrStopping)
                    if (!token.IsCancellationRequested) sample();
            await finishedOrStopping.ConfigureAwait(false);
        }
        finally
        {
            if (!completed.IsCompleted)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
                catch (Win32Exception) when (process.HasExited) { }
            }
            await completed.ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0 && !(allowWarningExit && process.ExitCode == 1))
        {
            string detail = string.Join(Environment.NewLine, outputTail.Concat(errorTail));
            throw new IOException($"{Path.GetFileName(info.FileName)} exited with code {process.ExitCode}." +
                (detail.Length == 0 ? "" : Environment.NewLine + detail));
        }
    }
}
