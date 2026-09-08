using System.Collections.Frozen;

namespace HerdDesk.Contracts;

// Missing or unknown operations stay absent. Do not encode them as tested-false bools.
public sealed record CapabilityProfile(
    string CliVersion,
    string? ServerVersion,
    int ApiSchemaProtocol,
    int SchemaVersion,
    string SchemaSha256,
    string RuntimeSchemaSha256Status,
    FrozenSet<string> VerifiedOperations)
{
    public bool HasOperation(string operation) =>
        !string.IsNullOrEmpty(operation) && VerifiedOperations.Contains(operation);

    public bool HasMutationControl =>
        VerifiedOperations.Overlaps(SchemaOperations.MutationControl);
}
