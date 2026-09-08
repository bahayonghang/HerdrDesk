using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Diagnostics;

public sealed class DiagnosticSinkOptions
{
    public int Capacity { get; init; } = 256;
    public int MaxFileBytes { get; init; } = 256 * 1024;
    public int MaxFiles { get; init; } = 4;
    public bool StartWriter { get; init; } = true;
}

public sealed class JsonlDiagnosticSink : IDiagnosticSink
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly object _gate = new();
    private readonly Queue<DiagnosticEvent> _queue = new();
    private readonly int _capacity;
    private readonly string _logFile;
    private readonly int _maxFileBytes;
    private readonly int _maxFiles;
    private readonly Thread? _writer;
    private long _dropped;
    private bool _completed;
    private int _disposed;

    public JsonlDiagnosticSink(string logFile, DiagnosticSinkOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logFile);
        var opts = options ?? new DiagnosticSinkOptions();
        if (opts.Capacity < 1 || opts.MaxFileBytes < 256 || opts.MaxFiles < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        _logFile = logFile;
        _capacity = opts.Capacity;
        _maxFileBytes = opts.MaxFileBytes;
        _maxFiles = opts.MaxFiles;
        if (opts.StartWriter)
        {
            _writer = new Thread(WriteLoop)
            {
                IsBackground = true,
                Name = "herddesk-diagnostics"
            };
            _writer.Start();
        }
    }

    public long DroppedCount => Interlocked.Read(ref _dropped);

    public bool TryWrite(DiagnosticEvent evt)
    {
        if (Volatile.Read(ref _disposed) != 0 || !DiagnosticEventValidator.IsValid(evt))
        {
            Interlocked.Increment(ref _dropped);
            return false;
        }
        lock (_gate)
        {
            if (_completed || _queue.Count >= _capacity)
            {
                Interlocked.Increment(ref _dropped);
                return false;
            }
            _queue.Enqueue(evt);
            Monitor.Pulse(_gate);
            return true;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return ValueTask.CompletedTask;
        lock (_gate)
        {
            _completed = true;
            Monitor.PulseAll(_gate);
        }
        _writer?.Join();
        DrainRemaining();
        return ValueTask.CompletedTask;
    }

    private void WriteLoop()
    {
        try
        {
            EnsureLogDirectory();
            while (true)
            {
                DiagnosticEvent evt;
                lock (_gate)
                {
                    while (_queue.Count == 0 && !_completed)
                        Monitor.Wait(_gate);
                    if (_queue.Count == 0)
                        return;
                    evt = _queue.Dequeue();
                }
                WriteOrDrop(evt);
            }
        }
        catch (Exception)
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    private void DrainRemaining()
    {
        try
        {
            EnsureLogDirectory();
        }
        catch (Exception)
        {
            Interlocked.Increment(ref _dropped);
            return;
        }
        while (true)
        {
            DiagnosticEvent evt;
            lock (_gate)
            {
                if (_queue.Count == 0)
                    return;
                evt = _queue.Dequeue();
            }
            WriteOrDrop(evt);
        }
    }

    private void EnsureLogDirectory()
    {
        var directory = Path.GetDirectoryName(_logFile);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }

    private void WriteOrDrop(DiagnosticEvent evt)
    {
        try
        {
            WriteLine(evt);
        }
        catch (Exception)
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    private void WriteLine(DiagnosticEvent evt)
    {
        var line = Format(evt);
        if (DiagnosticEventValidator.LooksSensitive(line))
        {
            Interlocked.Increment(ref _dropped);
            return;
        }
        RotateIfNeeded(line.Length + 1);
        File.AppendAllText(_logFile, line + "\n", Utf8NoBom);
    }

    private void RotateIfNeeded(int incomingBytes)
    {
        if (!File.Exists(_logFile))
            return;
        var info = new FileInfo(_logFile);
        if (info.Length + incomingBytes <= _maxFileBytes)
            return;
        for (var index = _maxFiles - 1; index >= 1; index--)
        {
            var dest = ArchivePath(index);
            var src = index == 1 ? _logFile : ArchivePath(index - 1);
            if (File.Exists(dest))
                File.Delete(dest);
            if (File.Exists(src))
                File.Move(src, dest);
        }
    }

    private string ArchivePath(int index) => _logFile + "." + index;

    internal static string Format(DiagnosticEvent evt)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("utc", evt.UtcTimestamp.UtcDateTime.ToString("o"));
            writer.WriteString("component", evt.Component);
            writer.WriteString("operation", evt.Operation);
            writer.WriteString("outcome", OutcomeName(evt.Outcome));
            if (evt.ErrorCode is not null)
                writer.WriteString("error_code", evt.ErrorCode);
            if (evt.Epoch is { } epoch)
                writer.WriteNumber("epoch", epoch);
            if (evt.DurationMs is { } duration)
                writer.WriteNumber("duration_ms", duration);
            if (evt.QueueBytes is { } queue)
                writer.WriteNumber("queue_bytes", queue);
            if (evt.DeviceAlias is not null)
                writer.WriteString("device_alias", evt.DeviceAlias);
            if (evt.SessionAlias is not null)
                writer.WriteString("session_alias", evt.SessionAlias);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string OutcomeName(DiagnosticOutcome outcome) => outcome switch
    {
        DiagnosticOutcome.Success => "success",
        DiagnosticOutcome.Failure => "failure",
        DiagnosticOutcome.Dropped => "dropped",
        DiagnosticOutcome.Unavailable => "unavailable",
        _ => "failure"
    };
}
