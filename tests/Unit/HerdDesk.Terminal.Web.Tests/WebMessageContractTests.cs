using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Terminal.Web;

internal static class WebMessageContractTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("allowlisted host and renderer kinds pass schema", AllowlistedKinds),
        ("unknown exec and rpc kinds are rejected", UnknownKinds),
        ("oversize input is rejected", Oversize),
        ("wrong epoch is rejected", WrongEpoch),
        ("unknown field is rejected", UnknownField),
        ("duplicate json key is rejected", DuplicateKey),
        ("unstable fault code is rejected", UnstableFaultCode),
        ("observe resize schema passes and policy denies", ObserveResizePolicy)
    ];

    static void AllowlistedKinds()
    {
        var epoch = WebTestHost.Epoch();
        WebTestHost.Check(WebMessageValidator.Evaluate(WebTestHost.Utf8Json(
            """{"version":1,"kind":"initialize","epoch":1,"theme":"dark","readOnly":true}"""), epoch).Accepted);
        var frame = WebMessageValidator.Evaluate(WebTestHost.Utf8Json(
            "{\"version\":1,\"kind\":\"frame\",\"epoch\":1,\"seq\":1,\"full\":true,\"bytes\":\"" +
            WebTestHost.B64(0x61) + "\"}"), epoch);
        WebTestHost.Check(frame.Accepted);
        WebTestHost.Check(frame.Bytes.Span[0] == 0x61);
        WebTestHost.Check(WebMessageValidator.Evaluate(WebTestHost.Utf8Json(
            """{"version":1,"kind":"parsed","epoch":1,"seq":1,"bytesConsumed":1}"""), epoch).Accepted);
        WebTestHost.Check(WebMessageValidator.Evaluate(WebTestHost.Utf8Json(
            "{\"version\":1,\"kind\":\"input\",\"epoch\":1,\"origin\":\"user_key\",\"bytes\":\"" +
            WebTestHost.B64(0x61) + "\"}"), epoch).Accepted);
        WebTestHost.Check(WebMessageValidator.Evaluate(WebTestHost.Utf8Json(
            """{"version":1,"kind":"resize","epoch":1,"cols":80,"rows":24,"cellPx":[8,16]}"""), epoch).Accepted);
        WebTestHost.Check(WebMessageValidator.Evaluate(WebTestHost.Utf8Json(
            """{"version":1,"kind":"linkRequest","epoch":1,"uri":"https://example.com","userGesture":true}"""),
            epoch).Accepted);
        WebTestHost.Check(WebMessageValidator.Evaluate(WebTestHost.Utf8Json(
            """{"version":1,"kind":"fault","epoch":1,"code":"renderer_crash"}"""), epoch).Accepted);
        WebTestHost.Check(WebMessageValidator.Evaluate(WebTestHost.Utf8Json(
            """{"version":1,"kind":"ready","epoch":1}"""), epoch).Accepted);
        WebTestHost.Check(WebMessageValidator.Evaluate(WebTestHost.Utf8Json(
            """{"version":2,"kind":"ready","epoch":1}"""), epoch).Code == "unsupported_web_message_version");
    }

    static void UnknownKinds()
    {
        var epoch = WebTestHost.Epoch();
        WebTestHost.Check(WebMessageValidator.Evaluate(WebTestHost.Utf8Json(
            """{"version":1,"kind":"host.exec","epoch":1}"""), epoch).Code == "unknown_web_message_type");
        WebTestHost.Check(WebMessageValidator.Evaluate(WebTestHost.Utf8Json(
            """{"version":1,"kind":"rpc.call","epoch":1}"""), epoch).Code == "unknown_web_message_type");
        WebTestHost.Check(WebMessageValidator.Evaluate(WebTestHost.Utf8Json(
            """{"version":1,"kind":"file.open","epoch":1}"""), epoch).Code == "unknown_web_message_type");
    }

    static void Oversize()
    {
        var epoch = WebTestHost.Epoch();
        var oversize = new byte[WebMessageLimits.MaxInputBytes + 1];
        var json = "{\"version\":1,\"kind\":\"input\",\"epoch\":1,\"origin\":\"user_key\",\"bytes\":\"" +
                   Convert.ToBase64String(oversize) + "\"}";
        WebTestHost.Check(WebMessageValidator.Evaluate(WebTestHost.Utf8Json(json), epoch).Code ==
                          "web_message_bytes_limit");
    }

    static void WrongEpoch()
    {
        var parsed = WebMessageValidator.Evaluate(WebTestHost.Utf8Json(
            """{"version":1,"kind":"ready","epoch":2}"""), WebTestHost.Epoch());
        WebTestHost.Check(!parsed.Accepted);
        WebTestHost.Check(parsed.Code == "stale_epoch");
    }

    static void UnknownField()
    {
        WebTestHost.Check(WebMessageValidator.Evaluate(WebTestHost.Utf8Json(
            """{"version":1,"kind":"ready","epoch":1,"pane":"p1"}"""), WebTestHost.Epoch()).Code ==
                          "unknown_web_message_field");
        WebTestHost.Check(WebMessageValidator.Evaluate(WebTestHost.Utf8Json(
            """{"version":1,"kind":"ready","epoch":1,"exec":"calc"}"""), WebTestHost.Epoch()).Code ==
                          "unknown_web_message_field");
    }

    static void DuplicateKey()
    {
        WebTestHost.Check(WebMessageValidator.Evaluate(WebTestHost.Utf8Json(
            """{"version":1,"kind":"ready","epoch":1,"kind":"fault"}"""), WebTestHost.Epoch()).Code ==
                          "duplicate_json_key");
    }

    static void UnstableFaultCode()
    {
        var epoch = WebTestHost.Epoch();
        WebTestHost.Check(WebMessageValidator.Evaluate(WebTestHost.Utf8Json(
            """{"version":1,"kind":"fault","epoch":1,"code":"renderer_crash"}"""), epoch).Accepted);
        WebTestHost.Check(WebMessageValidator.Evaluate(WebTestHost.Utf8Json(
            """{"version":1,"kind":"fault","epoch":1,"code":"NOT_STABLE"}"""), epoch).Code ==
                          "malformed_web_message");
        WebTestHost.Check(WebMessageValidator.Evaluate(WebTestHost.Utf8Json(
            """{"version":1,"kind":"fault","epoch":1,"code":"https://example.com/x"}"""), epoch).Code ==
                          "malformed_web_message");
    }

    static void ObserveResizePolicy()
    {
        var epoch = WebTestHost.Epoch();
        var parsed = WebMessageValidator.Evaluate(WebTestHost.Utf8Json(
            """{"version":1,"kind":"resize","epoch":1,"cols":80,"rows":24,"cellPx":[8,16]}"""), epoch);
        WebTestHost.Check(parsed.Accepted);
        var message = new WebMessage(
            "terminal.resize", parsed.Version, parsed.Epoch, WebTestHost.Pane(), 0,
            WebMessageDirection.RendererToHost);
        WebTestHost.Check(WebMessagePolicy.Evaluate(message, WebTestHost.Observe()).Code ==
                          "control_not_verified");
        WebTestHost.Check(WebMessagePolicy.Evaluate(message, WebTestHost.Control()).Allowed);
    }
}
