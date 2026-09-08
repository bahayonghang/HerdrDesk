namespace HerdDesk.Contracts;

public sealed record SchemaCompatibilityBinding(
    int Protocol,
    int SchemaVersion,
    string SchemaDocumentSha256,
    string SchemaDocumentProvenance,
    string RuntimeSchemaSha256Status,
    string CliVersion)
{
    public const int PinnedProtocol = 22;
    public const int PinnedSchemaVersion = 1;

    // Content SHA-256 of herdr v0.9.0 docs/next/api/herdr-api.schema.json
    // (commit b99002ac99b09e00b4ca692436cb15a6b0d676f1). Source document, not daemon proof.
    public const string PinnedDocumentSha256 =
        "5fb46b13fdaf39c88cf699b9806685868c7ee6b0142523d84391b1606416dc0a";

    public const string PinnedProvenance =
        "herdr GitHub v0.9.0 commit b99002ac99b09e00b4ca692436cb15a6b0d676f1 docs/next/api/herdr-api.schema.json content SHA-256; source document, not daemon proof";

    public const string RuntimeUnverified = "UNVERIFIED";

    public static SchemaCompatibilityBinding PinnedUnverified(string cliVersion) =>
        new(PinnedProtocol, PinnedSchemaVersion, PinnedDocumentSha256, PinnedProvenance,
            RuntimeUnverified, cliVersion);

    public static SchemaCompatibilityBinding PinnedMatchingRuntimeForTests(string cliVersion) =>
        new(PinnedProtocol, PinnedSchemaVersion, PinnedDocumentSha256, PinnedProvenance,
            PinnedDocumentSha256, cliVersion);

    public bool RuntimeHashMatchesDocument
    {
        get
        {
            if (string.Equals(RuntimeSchemaSha256Status, RuntimeUnverified, StringComparison.Ordinal))
                return false;
            return string.Equals(RuntimeSchemaSha256Status, SchemaDocumentSha256,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
