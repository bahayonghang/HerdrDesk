using HerdDesk.Infrastructure.Process;
using HerdDesk.Infrastructure.Ssh;

namespace HerdDesk.Infrastructure.SshTransports;

internal interface ISshChildProcessStarter
{
    OwnedChildProcess Start(SshProcessSpec spec);
}

internal sealed class OwnedSshChildProcessStarter : ISshChildProcessStarter
{
    public static OwnedSshChildProcessStarter Instance { get; } = new();

    public OwnedChildProcess Start(SshProcessSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return OwnedChildProcess.Start(spec.Executable, spec.Arguments);
    }
}
