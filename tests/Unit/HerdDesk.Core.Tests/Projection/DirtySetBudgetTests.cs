using HerdDesk.Core;

internal static class DirtySetBudgetTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("duplicate entity occupies one slot", DuplicateOneSlot),
        ("overflow clears keys and requires full resync", OverflowFullResync),
        ("snapshot clears the dirty set", SnapshotResets)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void DuplicateOneSlot()
    {
        var budget = new DirtySetBudget(4);
        Check(budget.Mark("pane:p1"));
        Check(budget.Mark("pane:p1"));
        Check(budget.Count == 1);
        Check(!budget.FullResyncRequired);
        Check(budget.Mark("tab:t1"));
        Check(budget.Count == 2);
    }

    static void OverflowFullResync()
    {
        var budget = new DirtySetBudget(2);
        Check(budget.Mark("a"));
        Check(budget.Mark("b"));
        Check(budget.Count == 2);
        Check(budget.Mark("c"));
        Check(budget.FullResyncRequired);
        Check(budget.Count == 0);
        Check(budget.Mark("d"));
        Check(budget.Count == 0);
        Check(budget.FullResyncRequired);
    }

    static void SnapshotResets()
    {
        var budget = new DirtySetBudget(2);
        budget.Mark("a");
        budget.Mark("b");
        budget.Mark("c");
        Check(budget.FullResyncRequired);
        budget.NoteSnapshotSucceeded();
        Check(!budget.FullResyncRequired);
        Check(budget.Count == 0);
        Check(budget.Mark("a"));
        Check(budget.Count == 1);
        Check(!budget.FullResyncRequired);
    }
}
