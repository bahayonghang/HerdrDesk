namespace HerdDesk.Infrastructure.Files;

public sealed class FileBridgeProtocolException : Exception
{
    public FileBridgeProtocolException(string code) : base(code)
    {
    }
}
