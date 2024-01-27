using System.Windows.Forms;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Sodiware.VisualStudio.Debugger;
using Sodiware.VisualStudio.Events;
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
    private Process? testHostProcess;
    private readonly IProjectThreadingService threading;
    private readonly AsyncLazy<IVsDebugger> debugger;
    private readonly IVsHierarchy projectHier;
    private readonly ILogger log;
    private bool disposed = true;
    private TaskCompletionSource<int> vsTestConsolePidTask = new();
    private SessionFlags flags;
    private uint cookie;

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


    internal DebugSession(ProcessStartInfo startInfo,
                          IProjectThreadingService threading,
                          AsyncLazy<IVsDebugger> debugger,
                          IVsHierarchy projectHier,
                          ILogger log)
    {
        this.log = log ?? Log.Logger;
        this.threading = threading;
        this.debugger = debugger;
        this.projectHier = projectHier;
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
    }

    public Task<int> StartAsync(CancellationToken cancellationToken)
    {
        this.WaitingForTestHostPid = true;
        this.process.Start();
        this.ProcessStarted = true;
        DebuggerEvents.Stop += onDebuggingStop;

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

    private void onDebuggingStop(object sender, EventArgs e)
    {
        this.Close();
    }

    public void Close()
    {
        log.LogVerbose("Closing DebugSession");
        DebuggerEvents.Stop -= onDebuggingStop;
        if (this.ProcessStarted)
        {
            try
            {
                this.killTestHostProcess();
            }
            catch (Exception ex)
            {
                log.LogError($"Error killing testhost process: {ex.Message}");
            }
            try
            {
                if (!this.process.HasExited)
                {
                    this.process.Kill();
                    this.process.Close();
                }
            }
            catch(Exception ex)
            {
                log.LogError($"Error Killing process: {ex.Message}");
            }
            finally
            {
                this.ProcessStarted = false;
                this.process.Dispose();
            }
        }
    }

    private void killTestHostProcess()
    {
        if (this.testHostProcess is not null
            && this.testHostProcess.HasExited == false)
        {
            try
            {
                this.testHostProcess.Kill();
                this.testHostProcess.Close();
                this.testHostProcess.Dispose();
            }
            finally
            {
                this.testHostProcess = null;
            }
        }
    }

    private void Process_Exited(object sender, EventArgs e)
    {
        this.log.LogInformation($"vstest.console Process exited ({this.process.ExitCode})");
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
                    log.LogVerbose($"TestHost PID: {pid}");
                    this.WaitingForTestHostPid = false;
                    this.testHostProcess = Process.GetProcessById(pid);
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

    internal void Add(uint[] pids) { }
}
