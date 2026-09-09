using System.Collections.Frozen;
using HerdDesk.Contracts;

namespace HerdDesk.Core;

public sealed class AttachmentCapabilityCatalog
{
    readonly FrozenDictionary<AttachmentCapabilityKey, CapabilityEvidence> _entries;

    public AttachmentCapabilityCatalog(IEnumerable<AttachmentCapabilityRecord>? records = null)
    {
        var map = new Dictionary<AttachmentCapabilityKey, CapabilityEvidence>();
        if (records is not null)
        {
            foreach (var record in records)
            {
                ArgumentNullException.ThrowIfNull(record);
                ArgumentNullException.ThrowIfNull(record.Key);
                ArgumentNullException.ThrowIfNull(record.Evidence);
                if (!record.Key.IsComplete)
                    continue;
                if (!map.TryAdd(record.Key, record.Evidence))
                    throw new ArgumentException(AttachmentCodes.CapabilityUnknown, nameof(records));
            }
        }

        _entries = map.Count == 0
            ? FrozenDictionary<AttachmentCapabilityKey, CapabilityEvidence>.Empty
            : map.ToFrozenDictionary();
    }

    public static AttachmentCapabilityCatalog Empty { get; } = new();

    public CapabilityEvidence Resolve(AttachmentCapabilityKey? key)
    {
        if (key is null || !key.IsComplete)
            return CapabilityEvidence.Unknown;
        return _entries.TryGetValue(key, out var evidence)
            ? evidence
            : CapabilityEvidence.Unknown;
    }
}
