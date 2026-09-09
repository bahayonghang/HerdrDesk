using System.Security.Cryptography;
using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Files;

internal static class LocalFileEndpointTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("local list empty space unicode", ListCases),
        ("local write zero and small hash", WriteHash),
        ("local fail does not overwrite", FailNoOverwrite),
        ("local keepboth exclusive", KeepBoth),
        ("local replace without observation rejected", ReplaceNoObs),
        ("local stale replace conflicts", StaleReplace),
        ("local cancel deletes only job temp", CancelTemp),
        ("local reserved name mapping required", ReservedMapping),
        ("local intermediate reparse is not followed", IntermediateReparse)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "herddesk-hd028-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static TransferEndpointKey Key() =>
        new(new DeviceId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")),
            new SessionKey(new DeviceId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")), "local", "dev"),
            new ConnectionEpoch(1), "local-root");

    static FileComponent Comp(string name) => new(Encoding.UTF8.GetBytes(name));

    static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    static LocalFileEndpoint Endpoint(string root) => new(root, Key());

    static void ListCases()
    {
        var root = TempRoot();
        try
        {
            var ep = Endpoint(root);
            var empty = ep.ListAsync(FileLocator.Root, 10, null).AsTask().GetAwaiter().GetResult();
            Check(empty.Succeeded);
            Check(empty.Entries is not null && empty.Entries.Count == 0);
            File.WriteAllText(Path.Combine(root, "hello world.txt"), "a");
            File.WriteAllText(Path.Combine(root, "你好.txt"), "b");
            var listed = ep.ListAsync(FileLocator.Root, 10, null).AsTask().GetAwaiter().GetResult();
            Check(listed.Succeeded);
            Check(listed.Entries!.Count == 2);
            foreach (var entry in listed.Entries)
            {
                Check(entry.DisplayName is "hello world.txt" or "你好.txt");
                Check(!entry.DisplayName.Contains('\\'));
                var st = ep.StatAsync(FileLocator.Root.Append(entry.Name), null).AsTask().GetAwaiter().GetResult();
                Check(st.Succeeded);
                Check(st.Stat!.Kind == FileEntryKind.File);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    static void WriteHash()
    {
        var root = TempRoot();
        try
        {
            var ep = Endpoint(root);
            WriteOne(ep, "empty.bin", []);
            WriteOne(ep, "small.bin", "hello"u8.ToArray());
            Check(File.ReadAllBytes(Path.Combine(root, "empty.bin")).Length == 0);
            Check(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, "small.bin"))) == "hello");
            Check(Directory.GetFiles(root, ".herddesk-upload-*").Length == 0);
        }
        finally { Directory.Delete(root, true); }
    }

    static void WriteOne(LocalFileEndpoint ep, string name, byte[] data)
    {
        var parent = ep.StatAsync(FileLocator.Root, null).AsTask().GetAwaiter().GetResult();
        Check(parent.Succeeded);
        var open = ep.BeginWriteAsync(new FileWriteRequest(
            Guid.NewGuid(), FileLocator.Root, Comp(name), FileConflictMode.Fail,
            parent.Stat!.Observation, null, (ulong)data.Length, Sha(data))).AsTask().GetAwaiter().GetResult();
        Check(open.Result.Succeeded);
        open.Session!.WriteAsync(data).AsTask().GetAwaiter().GetResult();
        var done = open.Session.CompleteAsync().AsTask().GetAwaiter().GetResult();
        Check(done.Succeeded);
        Check(done.Sha256 == Sha(data));
        open.Session.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    static void FailNoOverwrite()
    {
        var root = TempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "keep");
            var ep = Endpoint(root);
            var parent = ep.StatAsync(FileLocator.Root, null).AsTask().GetAwaiter().GetResult();
            var open = ep.BeginWriteAsync(new FileWriteRequest(
                Guid.NewGuid(), FileLocator.Root, Comp("a.txt"), FileConflictMode.Fail,
                parent.Stat!.Observation, null, 1, Sha("x"u8.ToArray()))).AsTask().GetAwaiter().GetResult();
            Check(open.Result.Succeeded);
            open.Session!.WriteAsync("x"u8.ToArray()).AsTask().GetAwaiter().GetResult();
            var done = open.Session.CompleteAsync().AsTask().GetAwaiter().GetResult();
            Check(!done.Succeeded);
            Check(done.Code == FileOpCodes.NameExists);
            Check(File.ReadAllText(Path.Combine(root, "a.txt")) == "keep");
            open.Session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Check(Directory.GetFiles(root, ".herddesk-upload-*").Length == 0);
        }
        finally { Directory.Delete(root, true); }
    }

    static void KeepBoth()
    {
        var root = TempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "keep");
            var ep = Endpoint(root);
            var parent = ep.StatAsync(FileLocator.Root, null).AsTask().GetAwaiter().GetResult();
            var data = "hello"u8.ToArray();
            var open = ep.BeginWriteAsync(new FileWriteRequest(
                Guid.NewGuid(), FileLocator.Root, Comp("a.txt"), FileConflictMode.KeepBoth,
                parent.Stat!.Observation, null, (ulong)data.Length, Sha(data))).AsTask().GetAwaiter().GetResult();
            Check(open.Result.Succeeded);
            open.Session!.WriteAsync(data).AsTask().GetAwaiter().GetResult();
            var done = open.Session.CompleteAsync().AsTask().GetAwaiter().GetResult();
            Check(done.Succeeded);
            Check(File.ReadAllText(Path.Combine(root, "a.txt")) == "keep");
            Check(File.ReadAllText(Path.Combine(root, "a (1).txt")) == "hello");
            open.Session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        finally { Directory.Delete(root, true); }
    }

    static void ReplaceNoObs()
    {
        var root = TempRoot();
        try
        {
            var ep = Endpoint(root);
            var parent = ep.StatAsync(FileLocator.Root, null).AsTask().GetAwaiter().GetResult();
            var open = ep.BeginWriteAsync(new FileWriteRequest(
                Guid.NewGuid(), FileLocator.Root, Comp("a.txt"), FileConflictMode.Replace,
                parent.Stat!.Observation, null, 0, Sha([]))).AsTask().GetAwaiter().GetResult();
            Check(!open.Result.Succeeded);
            Check(open.Result.Code == FileOpCodes.ReplaceObservationRequired);
        }
        finally { Directory.Delete(root, true); }
    }

    static void StaleReplace()
    {
        var root = TempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "old");
            var ep = Endpoint(root);
            var parent = ep.StatAsync(FileLocator.Root, null).AsTask().GetAwaiter().GetResult();
            var stale = new FileObservation(new string('a', 64));
            var open = ep.BeginWriteAsync(new FileWriteRequest(
                Guid.NewGuid(), FileLocator.Root, Comp("a.txt"), FileConflictMode.Replace,
                parent.Stat!.Observation, stale, 1, Sha("x"u8.ToArray()))).AsTask().GetAwaiter().GetResult();
            if (!ep.ReplaceSupported)
            {
                Check(!open.Result.Succeeded);
                Check(open.Result.Code is FileOpCodes.Unsupported or FileOpCodes.StaleTarget);
            }
            else
            {
                Check(!open.Result.Succeeded);
                Check(open.Result.Code == FileOpCodes.StaleTarget);
            }
            Check(File.ReadAllText(Path.Combine(root, "a.txt")) == "old");
        }
        finally { Directory.Delete(root, true); }
    }

    static void CancelTemp()
    {
        var root = TempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "source.txt"), "src");
            File.WriteAllText(Path.Combine(root, "sibling.txt"), "sib");
            var ep = Endpoint(root);
            var parent = ep.StatAsync(FileLocator.Root, null).AsTask().GetAwaiter().GetResult();
            var data = "payload"u8.ToArray();
            var open = ep.BeginWriteAsync(new FileWriteRequest(
                Guid.NewGuid(), FileLocator.Root, Comp("dest.txt"), FileConflictMode.Fail,
                parent.Stat!.Observation, null, (ulong)data.Length, Sha(data))).AsTask().GetAwaiter().GetResult();
            Check(open.Result.Succeeded);
            open.Session!.WriteAsync(data).AsTask().GetAwaiter().GetResult();
            var abort = open.Session.AbortAsync().AsTask().GetAwaiter().GetResult();
            Check(!abort.Succeeded);
            Check(File.ReadAllText(Path.Combine(root, "source.txt")) == "src");
            Check(File.ReadAllText(Path.Combine(root, "sibling.txt")) == "sib");
            Check(!File.Exists(Path.Combine(root, "dest.txt")));
            Check(Directory.GetFiles(root, ".herddesk-upload-*").Length == 0);
            open.Session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        finally { Directory.Delete(root, true); }
    }

    static void ReservedMapping()
    {
        var root = TempRoot();
        try
        {
            var ep = Endpoint(root);
            var parent = ep.StatAsync(FileLocator.Root, null).AsTask().GetAwaiter().GetResult();
            var open = ep.BeginWriteAsync(new FileWriteRequest(
                Guid.NewGuid(), FileLocator.Root, Comp("CON"), FileConflictMode.Fail,
                parent.Stat!.Observation, null, 0, Sha([]))).AsTask().GetAwaiter().GetResult();
            Check(!open.Result.Succeeded);
            Check(open.Result.Code == FileOpCodes.MappingRequired);
        }
        finally { Directory.Delete(root, true); }
    }

    static void IntermediateReparse()
    {
        var root = TempRoot();
        var outside = TempRoot();
        try
        {
            var nested = Path.Combine(outside, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "x.txt"), "out");
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(root, "link"), outside);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            var ep = Endpoint(root);
            var listed = ep.ListAsync(
                FileLocator.Root.Append(Comp("link")).Append(Comp("nested")),
                10, null).AsTask().GetAwaiter().GetResult();
            Check(!listed.Succeeded);
            Check(listed.Code == FileOpCodes.Unsupported);
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(outside, true);
        }
    }
}
