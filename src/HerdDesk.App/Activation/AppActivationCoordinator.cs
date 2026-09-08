namespace HerdDesk.App;

public sealed class AppActivationCoordinator : IDisposable, IConfigurationOwnership
{
    private readonly FileStream? _lockStream;
    private readonly string _intentPath;
    private int _disposed;
    private bool _pendingRedirect;

    private AppActivationCoordinator(bool isPrimary, FileStream? lockStream, string intentPath)
    {
        IsPrimary = isPrimary;
        _lockStream = lockStream;
        _intentPath = intentPath;
        CanWrite = isPrimary;
        WindowCount = 1;
    }

    public bool IsPrimary { get; }
    public bool CanWrite { get; }
    public bool OwnsConfigurationWriter => CanWrite;
    public int WindowCount { get; }
    public ActivationIntent? LastRedirected { get; private set; }
    public bool AcceptingActivation { get; set; } = true;

    public static AppActivationCoordinator Claim(string instanceName, string intentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(intentPath);
        var directory = Path.GetDirectoryName(intentPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var lockPath = Path.Combine(directory ?? ".", instanceName + ".lock");
        FileStream? stream = null;
        try
        {
            stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return new AppActivationCoordinator(false, null, intentPath);
        }
        catch (UnauthorizedAccessException)
        {
            return new AppActivationCoordinator(false, null, intentPath);
        }

        return new AppActivationCoordinator(true, stream, intentPath);
    }

    public bool Redirect(ActivationIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (IsPrimary)
        {
            LastRedirected = intent;
            _pendingRedirect = true;
            return true;
        }

        var directory = Path.GetDirectoryName(_intentPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var temp = _intentPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, intent.ToUtf8());
        File.Move(temp, _intentPath, overwrite: true);
        return true;
    }

    public ActivationIntent? PollRedirect()
    {
        if (!IsPrimary || !AcceptingActivation)
            return null;
        if (File.Exists(_intentPath))
        {
            try
            {
                var bytes = File.ReadAllBytes(_intentPath);
                File.Delete(_intentPath);
                if (!ActivationIntent.TryParse(bytes, out var intent, out _))
                    return null;
                LastRedirected = intent;
                _pendingRedirect = false;
                return intent;
            }
            catch (IOException)
            {
                return null;
            }
        }

        if (!_pendingRedirect)
            return null;
        _pendingRedirect = false;
        return LastRedirected;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _lockStream?.Dispose();
    }
}
