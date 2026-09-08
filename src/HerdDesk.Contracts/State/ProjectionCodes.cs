namespace HerdDesk.Contracts;

public static class ProjectionCodes
{
    public const string RequiredFieldMissing = "rpc_required_field_missing";
    public const string FieldTypeInvalid = "rpc_field_type_invalid";
    public const string DuplicateIdentity = "rpc_duplicate_identity";
    public const string ParentMissing = "rpc_parent_missing";
    public const string SchemaIncompatible = "rpc_schema_incompatible";
    public const string StaleEpoch = "stale_epoch";
    public const string FullSnapshotRequired = "full_snapshot_required";
    public const string ErrorEnvelope = "rpc_error_envelope";
    public const string DuplicateJsonKey = "duplicate_json_key";
    public const string InvalidIdentity = "invalid_identity";
}
