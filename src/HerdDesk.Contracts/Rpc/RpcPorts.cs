using System.Text.Json;

namespace HerdDesk.Contracts;

public readonly record struct RpcRequestId(ConnectionEpoch Epoch, ulong Value);

public enum RpcFailureKind
{
    NotSent,
    CancelledAfterWrite,
    ConnectionLost,
    Protocol,
    SubscribeAckFailed,
    EventQueueOverflow,
    Unavailable
}

public static class RpcCodes
{
    public const string Unavailable = "rpc_bridge_unavailable";
    public const string EndpointInvalid = "bridge_endpoint_invalid";
    public const string ConnectDenied = "bridge_connect_denied";
    public const string ConnectFailed = "bridge_connect_failed";
    public const string ProtocolPollution = "rpc_protocol_pollution";
    public const string EnvelopeInvalid = "rpc_envelope_invalid";
    public const string LineBytesLimit = "line_bytes_limit";
    public const string TruncatedRecord = "truncated_ndjson_record";
    public const string DuplicateJsonKey = "duplicate_json_key";
    public const string ConnectionLost = "rpc_connection_lost";
    public const string UnknownResponse = "rpc_unknown_response_id";
    public const string DuplicateResponse = "rpc_duplicate_response_id";
    public const string IdOverflow = "rpc_request_id_overflow";
    public const string SubscribeAckFailed = "rpc_subscribe_ack_failed";
    public const string EventQueueOverflow = "rpc_event_queue_overflow";
    public const string ChildExited = "rpc_child_exited";
    public const string NotSent = "rpc_not_sent";
    public const string CancelledAfterWrite = "rpc_cancelled_after_write";
    public const string BinaryClientRejected = "rpc_binary_client_socket_rejected";
    public const string RemotePipeRejected = "remote_unc_rejected";
    public const string ExecutableInvalid = "rpc_bridge_executable_invalid";
}

public sealed record RpcFailure(string Code, RpcFailureKind Kind);

public sealed class RpcRequestOutcome : IDisposable
{
    public RpcRequestOutcome(JsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
    }

    public RpcRequestOutcome(RpcFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Failure = failure;
    }

    public JsonDocument? Document { get; }
    public RpcFailure? Failure { get; }
    public bool Succeeded => Document is not null && Failure is null;

    public void Dispose() => Document?.Dispose();
}

public interface IRpcRequestConnection : IAsyncDisposable
{
    ConnectionEpoch Epoch { get; }
    int PendingCount { get; }
    int? ChildProcessId { get; }
    ValueTask<RpcRequestOutcome> RequestAsync(
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken = default);
}

public interface IRpcSubscriptionConnection : IAsyncDisposable
{
    ConnectionEpoch Epoch { get; }
    int? ChildProcessId { get; }
    RpcFailure? Failure { get; }
    IAsyncEnumerable<JsonElement> ReadEventsAsync(CancellationToken cancellationToken = default);
}

public interface IRpcConnectionFactory : IAdapterCapability
{
    ValueTask<IRpcRequestConnection?> OpenRequestAsync(
        SessionKey session,
        ConnectionEpoch epoch,
        string socketPath,
        CancellationToken cancellationToken = default);

    ValueTask<IRpcSubscriptionConnection?> OpenSubscriptionAsync(
        SessionKey session,
        ConnectionEpoch epoch,
        string socketPath,
        JsonElement subscribeParameters,
        CancellationToken cancellationToken = default);
}
