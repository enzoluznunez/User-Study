using UnityEngine;

public static class AgentTurn
{
    public static bool Marked { get; private set; }
    public static bool UserTookOver { get; private set; }

    // The serial of the first edit this turn could have made. Edits leaving
    // the timeline mid-turn (a dataset switch) do not move it.
    private static long? _baseline;

    public static void NoteToolCall()
    {
        Marked = true;
        if (_baseline == null) _baseline = EditList.Active.NextSerial;
    }

    private static long Baseline => _baseline ?? EditList.Active.NextSerial;

    public static int AppliedSoFar() => Marked ? EditList.Active.CountSince(Baseline) : 0;

    public static void UserTookControl(string reason)
    {
        foreach (IView view in Scene.Views) view.Interrupt();

        UserTookOver = true;

        int applied = AppliedSoFar();
        if (applied <= 0) return;

        int stamped = EditList.Active.StampGroupSince(Baseline);
        if (stamped <= 0) return;

        StateChannel.Record("Assistant",
            $"the user stopped you after {stamped} of your changes had been applied");

        ManageTools tools = Scene.Tools;
        if (tools != null)
            Notices.Show(tools, "Assistant Stopped", stamped == 1
                ? "One change was already made. Undo takes it back."
                : $"{stamped} changes were already made. Undo takes them back in one step.");
    }

    public static void Clear()
    {
        Marked = false;
        UserTookOver = false;
        _baseline = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Clear();
}
