var cases = new (string Name, Action Run)[] { };
cases =
[
    .. WebMessageContractTests.All,
    .. RenderFlowControllerTests.All,
    .. WebTerminalRendererTests.All,
    .. WebViewSecurityTests.All,
    .. CompositionDedupTests.All,
    .. KeySequenceTranslatorTests.All,
    .. FocusRaceTests.All,
    .. SelectionMousePolicyTests.All,
    .. OscClipboardPolicyTests.All
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

Console.WriteLine(
    $"{cases.Length - failed}/{cases.Length} terminal web unit tests passed; no live WebView2/IME validation.");
return failed == 0 ? 0 : 1;
