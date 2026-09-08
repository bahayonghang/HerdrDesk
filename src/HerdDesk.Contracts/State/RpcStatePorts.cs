using System.Text.Json;

namespace HerdDesk.Contracts;

public interface IRpcStateDecoder
{
    DecodeResult<DecodedSessionSnapshot> DecodeSnapshot(
        JsonElement document,
        SessionKey session,
        ConnectionEpoch epoch,
        SchemaCompatibilityBinding binding);

    DecodeResult<DecodedRpcEvent> DecodeEvent(
        JsonElement document,
        SessionKey session,
        ConnectionEpoch epoch,
        SchemaCompatibilityBinding binding);
}
