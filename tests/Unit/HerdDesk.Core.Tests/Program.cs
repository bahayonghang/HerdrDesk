using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Core;

static void Check(bool condition)
{
    if (!condition)
        throw new Exception("assertion_failed");
}

static PaneKey Pane(string pane = "p1") =>
    new(new SessionKey(new DeviceId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")), "endpoint", "dev"),
        "ws", pane);

static InputContext Context(
    TerminalAccess access = TerminalAccess.Controlling,
    bool verified = true,
    long epoch = 1,
    string pane = "p1") =>
    new(Pane(pane), new ConnectionEpoch(epoch), access, verified);

static RendererInput Input(
    InputOrigin origin = InputOrigin.CommittedText,
    long epoch = 1,
    string pane = "p1",
    byte[]? bytes = null) =>
    new(Pane(pane), new ConnectionEpoch(epoch), origin, bytes ?? Encoding.UTF8.GetBytes("ok"));

var cases = new (string Name, Action Run)[]
{
    ("input policy allows verified controlling committed text", () =>
    {
        var decision = InputPolicy.Evaluate(Context(), Input());
        Check(decision.Allowed);
        Check(decision.Code == "allowed");
    }),
    ("input policy rejects observing and unverified control", () =>
    {
        Check(InputPolicy.Evaluate(Context(TerminalAccess.Observing, false), Input()).Code ==
              "control_not_verified");
        Check(InputPolicy.Evaluate(Context(verified: false), Input()).Code == "control_not_verified");
        Check(InputPolicy.Evaluate(Context(), Input(epoch: 2)).Code == "stale_epoch");
        Check(InputPolicy.Evaluate(Context(), Input(pane: "other")).Code == "wrong_pane");
        Check(InputPolicy.Evaluate(Context(), Input(InputOrigin.EmulatorReply)).Code == "input_origin_denied");
    }),
    ("parser latches after malformed record", () =>
    {
        var parser = new TerminalFrameParser();
        try
        {
            parser.Parse("{"u8.ToArray());
            throw new Exception("expected_reject");
        }
        catch (TerminalProtocolException error) when (error.Message == "malformed_terminal_record")
        {
        }
        try
        {
            parser.Parse("""{"type":"terminal.frame","seq":1,"encoding":"ansi","width":80,"height":24,"full":true,"bytes":"YQ=="}"""u8.ToArray());
            throw new Exception("expected_latch");
        }
        catch (TerminalProtocolException error) when (error.Message == "terminal_stream_not_active")
        {
        }
    }),
    ("core assembly does not reference winui webview2 ssh or tests", () =>
    {
        var names = typeof(InputPolicy).Assembly.GetReferencedAssemblies().Select(item => item.Name!).ToArray();
        Check(names.Contains("HerdDesk.Contracts"));
        foreach (var name in names)
        {
            var lower = name.ToLowerInvariant();
            Check(!lower.Contains("windowsappsdk"));
            Check(!lower.Contains("webview2"));
            Check(!lower.Contains("winui"));
            Check(!lower.Contains("ssh"));
            Check(!lower.Contains("herddesk.app"));
            Check(!lower.Contains("tests"));
            Check(!lower.Contains("herddesk.infrastructure"));
        }
    }),
};

cases =
[
    .. cases,
    .. ProjectionCases.All,
    .. DeviceSessionCases.All,
    .. BaselineSuppressionTests.All,
    .. TransitionDedupTests.All,
    .. UnreadAggregationTests.All,
    .. StaleAndUnknownTests.All
];

var failed = 0;
foreach (var test in cases)
{
    try
    {
        test.Run();
        Console.WriteLine("PASS " + test.Name);
    }
    catch (Exception error)
    {
        failed++;
        Console.Error.WriteLine("FAIL " + test.Name + ": " + error.GetType().Name + " " + error.Message);
    }
}
Console.WriteLine($"{cases.Length - failed}/{cases.Length} core unit tests passed; no live Windows/daemon validation.");
return failed == 0 ? 0 : 1;
