using System.Collections.Frozen;
using HerdDesk.Contracts;

namespace HerdDesk.Core;

public static class CapabilityGate
{
    public static CapabilityProfile Evaluate(
        SchemaCompatibilityBinding binding,
        int observedProtocol,
        string? serverVersion)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var compatible =
            binding.Protocol == SchemaCompatibilityBinding.PinnedProtocol &&
            binding.SchemaVersion == SchemaCompatibilityBinding.PinnedSchemaVersion &&
            observedProtocol == binding.Protocol &&
            observedProtocol == SchemaCompatibilityBinding.PinnedProtocol &&
            binding.RuntimeHashMatchesDocument &&
            string.Equals(binding.SchemaDocumentSha256, SchemaCompatibilityBinding.PinnedDocumentSha256,
                StringComparison.OrdinalIgnoreCase);
        var operations = compatible
            ? SchemaOperations.VerifiedWhenCompatible
            : FrozenSet<string>.Empty;
        return new CapabilityProfile(
            binding.CliVersion,
            serverVersion,
            binding.Protocol,
            binding.SchemaVersion,
            binding.SchemaDocumentSha256,
            binding.RuntimeSchemaSha256Status,
            operations);
    }
}
