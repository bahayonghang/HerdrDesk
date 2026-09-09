using HerdDesk.Contracts;

namespace HerdDesk.Core;

public sealed class AttachmentCacheLease : IDisposable
{
    readonly Action<Guid> _onRelease;

    internal AttachmentCacheLease(Guid draftId, Action<Guid> onRelease)
    {
        DraftId = draftId;
        _onRelease = onRelease;
    }

    public Guid DraftId { get; }
    public bool Released { get; private set; }

    public void Dispose()
    {
        if (Released)
            return;
        Released = true;
        _onRelease(DraftId);
    }
}

public sealed class AttachmentLeaseRegistry
{
    readonly Dictionary<Guid, AttachmentCacheLease> _held = [];

    public int HeldCount => _held.Count;

    public bool IsHeld(Guid draftId) => _held.ContainsKey(draftId);

    public AttachmentCacheLease Acquire(Guid draftId)
    {
        if (draftId == Guid.Empty)
            throw new ArgumentException(AttachmentCodes.Empty, nameof(draftId));
        if (_held.TryGetValue(draftId, out var existing) && !existing.Released)
            return existing;
        var lease = new AttachmentCacheLease(draftId, id => _held.Remove(id));
        _held[draftId] = lease;
        return lease;
    }

    public void Release(Guid draftId)
    {
        if (_held.TryGetValue(draftId, out var lease))
            lease.Dispose();
    }
}
