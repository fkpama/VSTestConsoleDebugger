using System.Windows.Forms;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Threading;
using Sodiware.VisualStudio.Logging;

namespace Launcher.Debugger;

internal class DebugSession : IDisposable
{
    const string s_processIdPattern = "Process Id: ";
    const string s_namePattern = ", Name: ";
    enum SessionFlags
    {
        WaitingForTestHostPid = 1,
        Launched              = 1 << 1
    }
    private readonly Process process;
    private readonly ILogger log;
    private bool disposed = true;
    private TaskCompletionSource<int> vsTestConsolePidTask = new();
    private SessionFlags flags;

    bool ProcessStarted
    {
        get => this.flags.HasFlag(SessionFlags.Launched);
        set => this.setFlags(SessionFlags.Launched, value);
    }

    private bool WaitingForTestHostPid
    {
        get => this.flags.HasFlag(SessionFlags.WaitingForTestHostPid);
        set => this.setFlags(SessionFlags.WaitingForTestHostPid, value);
    }

    internal DebugSession(ProcessStartInfo startInfo, ILogger log)
    {
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        this.process = new()
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };
        this.process.OutputDataReceived += this.Process_OutputDataReceived;
        this.process.ErrorDataReceived += this.Process_ErrorDataReceived;
        this.process.Exited += this.Process_Exited;
        this.log = log;
    }

    public Task<int> StartAsync(CancellationToken cancellationToken)
    {
        this.WaitingForTestHostPid = true;
        this.process.Start();
        this.ProcessStarted = true;

        this.process.BeginOutputReadLine();
        this.process.BeginErrorReadLine();
        var timeoutTask = this.vsTestConsolePidTask.Task;
        if (!System.Diagnostics.Debugger.IsAttached)
        {
            timeoutTask = timeoutTask.WithTimeout(TimeSpan.FromSeconds(5));
            _ = timeoutTask.ContinueWith(t =>
            {
                if (!this.process.HasExited)
                {
                    this.process.Kill();
                    this.Close();
                }
            }, cancellationToken, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
        return timeoutTask.WithCancellationToken(cancellationToken);
    }

    private void Close()
    {
        if (this.ProcessStarted)
        {
            this.process.Close();
            this.process.Dispose();
            this.ProcessStarted = false;
        }
    }

    private void Process_Exited(object sender, EventArgs e)
    {
        this.ProcessStarted = false;
    }

    private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        this.log.LogError($"[STDERR] {e.Data}");
    }

    private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null) return;
        this.log.LogInformation($"[STDOUT] {e.Data}");
        if (this.WaitingForTestHostPid)
        {
            var line = e.Data.Trim();
            var idx = line.IndexOf(s_processIdPattern, StringComparison.Ordinal);
            if (idx >= 0)
            {
                idx += s_processIdPattern.Length;
                var idx2 = line.IndexOf(s_namePattern, idx + 1, StringComparison.Ordinal);

                var str = line.Substring(idx, idx2 - idx)?.Trim();
                if (str?.Length > 0 && str.All(char.IsDigit))
                {
                    var pid = int.Parse(str);
                    this.WaitingForTestHostPid = false;
                    this.vsTestConsolePidTask.SetResult(pid);
                }
            }

        }
    }

    public void Dispose()
    {
        this.Close();
        GC.SuppressFinalize(this);
    }
    private void setFlags(SessionFlags flag, bool value)
                => this.flags = value ? this.flags | flag : this.flags & ~flag;
}
