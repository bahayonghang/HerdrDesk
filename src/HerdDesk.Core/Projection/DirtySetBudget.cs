namespace HerdDesk.Core;

public sealed class DirtySetBudget
{
    public const int DefaultMaxKeys = 1024;
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly int _maxKeys;

    public DirtySetBudget(int maxKeys = DefaultMaxKeys)
    {
        if (maxKeys < 1)
            throw new ArgumentOutOfRangeException(nameof(maxKeys));
        _maxKeys = maxKeys;
    }

    public int Count
    {
        get { lock (_gate) return _keys.Count; }
    }

    private bool _fullResync;

    public bool FullResyncRequired
    {
        get { lock (_gate) return _fullResync; }
    }

    public IReadOnlyCollection<string> Keys
    {
        get
        {
            lock (_gate)
                return [.. _keys];
        }
    }

    public bool Mark(string entityKey)
    {
        if (string.IsNullOrWhiteSpace(entityKey))
            return false;
        lock (_gate)
        {
            if (_fullResync)
                return true;
            if (_keys.Contains(entityKey))
                return true;
            if (_keys.Count >= _maxKeys)
            {
                _keys.Clear();
                _fullResync = true;
                return true;
            }

            _keys.Add(entityKey);
            return true;
        }
    }

    public void NoteSnapshotSucceeded()
    {
        lock (_gate)
        {
            _keys.Clear();
            _fullResync = false;
        }
    }
}
