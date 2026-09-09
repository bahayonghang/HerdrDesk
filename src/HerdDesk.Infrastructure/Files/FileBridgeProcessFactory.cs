namespace HerdDesk.Infrastructure.Files;

public sealed class FileBridgeProcessFactory
{
    readonly string _executable;

    public FileBridgeProcessFactory(string executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        _executable = executable;
    }

    public FileBridgeClient Start(string workingDirectory) =>
        FileBridgeClient.Start(_executable, workingDirectory);
}
