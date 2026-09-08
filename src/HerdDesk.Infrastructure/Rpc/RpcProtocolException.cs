namespace HerdDesk.Infrastructure.Rpc;

public sealed class RpcProtocolException : Exception
{
    public RpcProtocolException(string code) : base(code)
    {
    }
}
