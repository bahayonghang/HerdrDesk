using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Core;

// BCL-only G0 smoke runner; dotnet run, not dotnet test. The Windows UI and
// upstream daemon are not exercised. No external NuGet packages are needed.
static byte[] Frame(ulong sequence = 1, bool full = true, byte[]? bytes = null) =>
    JsonSerializer.SerializeToUtf8Bytes(new { type = "terminal.frame", seq = sequence,
        encoding = "ansi", width = 120, height = 40, full,
        bytes = Convert.ToBase64String(bytes ?? Encoding.UTF8.GetBytes("hello")) });
static void Check(bool condition) { if (!condition) throw new Exception("assertion_failed"); }
static void Reject(Action action)
{
    try { action(); }
    catch (TerminalProtocolException) { return; }
    throw new Exception("expected_protocol_rejection");
}

var pane = new PaneKey(new SessionKey(new DeviceId(Guid.NewGuid()), "test-only-api", "test"), "w1", "p1");
var epoch = new ConnectionEpoch(1);
var context = new InputContext(pane, epoch, TerminalAccess.Controlling, ControlVerified: true);
var request = new RendererInput(pane, epoch, InputOrigin.CommittedText, Encoding.UTF8.GetBytes("你好"));
var cases = new (string Name, Action Run)[]
{
    ("parse full frame", () => Check(new TerminalFrameParser().Parse(Frame()) is TerminalFrame { Sequence: 1, Full: true })),
    ("ordered delta", () => { var p = new TerminalFrameParser(); p.Parse(Frame()); Check(p.Parse(Frame(2, false)) is TerminalFrame { Sequence: 2 }); }),
    ("initial delta rejected", () => Reject(() => new TerminalFrameParser().Parse(Frame(full: false)))),
    ("gap rejected", () => { var p = new TerminalFrameParser(); p.Parse(Frame()); Reject(() => p.Parse(Frame(3))); }),
    ("failure latched", () => { var p = new TerminalFrameParser(); Reject(() => p.Parse("{}"u8.ToArray())); Reject(() => p.Parse(Frame())); }),
    ("replay rejected", () => { var p = new TerminalFrameParser(); p.Parse(Frame()); Reject(() => p.Parse(Frame())); }),
    ("duplicate key rejected", () => Reject(() => new TerminalFrameParser().Parse("{\"type\":\"terminal.closed\",\"type\":\"terminal.closed\"}"u8.ToArray()))),
    ("invalid utf8 rejected", () => Reject(() => new TerminalFrameParser().Parse(new byte[] { 255 }))),
    ("utf8 frame split preserved", () => { var p = new TerminalFrameParser(); var f = (TerminalFrame)p.Parse(Frame(bytes: new byte[] { 0xe4 })); Check(f.Bytes.Span[0] == 0xe4); }),
    ("closed envelope", () => Check(new TerminalFrameParser().Parse("{\"type\":\"terminal.closed\",\"reason\":\"detached\"}"u8.ToArray()) is TerminalClosed { ReasonPresent: true })),
    ("frame after close rejected", () => { var p = new TerminalFrameParser(); p.Parse("{\"type\":\"terminal.closed\"}"u8.ToArray()); Reject(() => p.Parse(Frame())); }),
    ("max uint64 sequence", () => Check(new TerminalFrameParser().Parse(Frame(ulong.MaxValue)) is TerminalFrame { Sequence: ulong.MaxValue })),
    ("verified exact input allowed", () => Check(InputPolicy.Evaluate(context, request).Allowed)),
    ("unverified control denied", () => Check(!InputPolicy.Evaluate(context with { ControlVerified = false }, request).Allowed)),
    ("observe denied", () => Check(!InputPolicy.Evaluate(context with { Access = TerminalAccess.Observing }, request).Allowed)),
    ("old epoch denied", () => Check(!InputPolicy.Evaluate(context, request with { Epoch = new ConnectionEpoch(2) }).Allowed)),
    ("other pane denied", () => Check(!InputPolicy.Evaluate(context, request with { Pane = pane with { PaneId = "p2" } }).Allowed)),
    ("emulator reply denied", () => Check(!InputPolicy.Evaluate(context, request with { Origin = InputOrigin.EmulatorReply }).Allowed)),
    ("empty input denied", () => Check(!InputPolicy.Evaluate(context, request with { Bytes = ReadOnlyMemory<byte>.Empty }).Allowed)),
    ("oversize input denied", () => Check(!InputPolicy.Evaluate(context, request with { Bytes = new byte[InputPolicy.MaxInputBytes + 1] }).Allowed)),
    ("unknown origin denied", () => Check(!InputPolicy.Evaluate(context, request with { Origin = (InputOrigin)999 }).Allowed)),
    ("default identity denied", () => Check(!InputPolicy.Evaluate(context with { ActivePane = default }, request with { Pane = default }).Allowed)),
};
int failed = 0;
foreach (var test in cases)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {error.GetType().Name}"); }
}
Console.WriteLine($"{cases.Length - failed}/{cases.Length} smoke tests passed; no live Windows/daemon validation.");
return failed == 0 ? 0 : 1;
