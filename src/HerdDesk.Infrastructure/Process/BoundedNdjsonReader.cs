using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Rpc;

namespace HerdDesk.Infrastructure.Process;

public sealed class BoundedNdjsonReader
{
    public const int MaxLineBytes = 16 * 1024 * 1024;
    private readonly Stream _stream;
    private readonly byte[] _chunk = new byte[64 * 1024];
    private readonly List<byte> _pending = [];
    private bool _failed;

    public BoundedNdjsonReader(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
    }

    public async ValueTask<byte[]?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        if (_failed)
            throw new RpcProtocolException(RpcCodes.ConnectionLost);
        while (true)
        {
            var index = _pending.IndexOf((byte)'\n');
            if (index >= 0)
            {
                if (index > MaxLineBytes)
                {
                    _failed = true;
                    throw new RpcProtocolException(RpcCodes.LineBytesLimit);
                }
                var line = _pending.GetRange(0, index).ToArray();
                _pending.RemoveRange(0, index + 1);
                return line;
            }

            if (_pending.Count > MaxLineBytes)
            {
                _failed = true;
                throw new RpcProtocolException(RpcCodes.LineBytesLimit);
            }

            var read = await _stream.ReadAsync(_chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (_pending.Count > 0)
                {
                    _failed = true;
                    throw new RpcProtocolException(RpcCodes.TruncatedRecord);
                }
                return null;
            }

            if (_pending.Count > MaxLineBytes - read)
            {
                _failed = true;
                throw new RpcProtocolException(RpcCodes.LineBytesLimit);
            }
            _pending.AddRange(_chunk.AsSpan(0, read).ToArray());
        }
    }
}
