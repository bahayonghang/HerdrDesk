using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Files;

internal static class FileBridgeClientTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("filebridge client pollution fails closed", Pollution),
        ("filebridge client early eof without complete is outcome_unknown", EarlyEof)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static string Exe()
    {
        var path = Environment.ProcessPath!;
        Check(Path.IsPathFullyQualified(path));
        return path;
    }

    static void Pollution()
    {
        var client = FileBridgeClient.Start(Exe(), ["--fake-filebridge", "pollution"]);
        try
        {
            var result = client.ListAsync(FileLocator.Root, 1, null).AsTask().GetAwaiter().GetResult();
            Check(!result.Succeeded);
            Check(result.Code is FileBridgeCodes.ProtocolPollution or FileOpCodes.ProtocolPollution
                or FileBridgeCodes.TruncatedHeader or FileOpCodes.OutcomeUnknown);
        }
        finally
        {
            client.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void EarlyEof()
    {
        var client = FileBridgeClient.Start(Exe(), ["--fake-filebridge", "eof"]);
        try
        {
            var result = client.ListAsync(FileLocator.Root, 1, null).AsTask().GetAwaiter().GetResult();
            Check(!result.Succeeded);
            Check(result.Code == FileOpCodes.OutcomeUnknown);
            Check(client.Outcome is FileBridgeOutcome.Unknown or FileBridgeOutcome.Pending or FileBridgeOutcome.Failed);
        }
        finally
        {
            client.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
