using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Rpc;

public static class RpcSocketPath
{
    public static string? RejectReason(string? socketPath)
    {
        if (string.IsNullOrWhiteSpace(socketPath))
            return RpcCodes.EndpointInvalid;
        if (socketPath.IndexOf('\0') >= 0 || socketPath.IndexOf('\r') >= 0 || socketPath.IndexOf('\n') >= 0)
            return RpcCodes.EndpointInvalid;
        if (IsRemoteSmb(socketPath))
            return RpcCodes.RemotePipeRejected;
        if (IsBinaryClientSocket(socketPath))
            return RpcCodes.BinaryClientRejected;
        return null;
    }

    private static bool IsRemoteSmb(string path)
    {
        var normalized = path.Replace('/', '\\');
        if (!normalized.StartsWith(@"\\", StringComparison.Ordinal))
            return false;
        return !normalized.StartsWith(@"\\.\", StringComparison.Ordinal) &&
               !normalized.StartsWith(@"\\?\", StringComparison.Ordinal);
    }

    private static bool IsBinaryClientSocket(string path)
    {
        var name = Path.GetFileName(path);
        if (string.Equals(name, "herdr-client.sock", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.EndsWith("-client.sock", StringComparison.OrdinalIgnoreCase))
            return true;
        return path.Contains("herdr-client.sock", StringComparison.OrdinalIgnoreCase);
    }
}
