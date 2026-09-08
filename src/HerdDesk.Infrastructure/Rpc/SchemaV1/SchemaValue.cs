using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Rpc.SchemaV1;

public readonly record struct SchemaValue<TKnown>(string Raw, TKnown? Known)
    where TKnown : struct, Enum
{
    public WireEnum<TKnown> ToWire() => new(Raw, Known);
}
