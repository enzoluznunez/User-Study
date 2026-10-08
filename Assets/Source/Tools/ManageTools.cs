using System;
using UnityEngine;

public enum ToolType { None, Filter, Move, Sort, Rotate, Scale, Profile }

public class ManageTools : MonoBehaviour
{
    public event Action<ToolType> OnToolChanged;

    public event Action<ToolType> OnToolReset;


    public ToolType SelectedTool { get; private set; } = ToolType.None;

    private static bool UserDriven => StateChannel.UserDriven;

    public void SelectTool(ToolType tool)
    {
        if (UserDriven) AgentTurn.UserTookControl("tool changed");
        ToolType next = SelectedTool == tool ? ToolType.None : tool;
        if (next == SelectedTool) return;

        SelectedTool = next;
        OnToolChanged?.Invoke(SelectedTool);
        ReportSelection();
    }

    public void DeselectTool()
    {
        if (UserDriven) AgentTurn.UserTookControl("tool deselected");
        if (SelectedTool == ToolType.None) return;

        SelectedTool = ToolType.None;
        OnToolChanged?.Invoke(SelectedTool);
        ReportSelection();
    }

    private void ReportSelection() => StateChannel.RecordState("tool",
        SelectedTool == ToolType.None ? "no tool is selected" : $"the {SelectedTool} tool is selected");

    private ToolType _suspended = ToolType.None;

    public void SuspendTool()
    {
        _suspended = SelectedTool;
        DeselectTool();
    }

    public void ResumeTool()
    {
        if (_suspended == ToolType.None) return;
        ToolType resume = _suspended;
        _suspended = ToolType.None;
        SelectTool(resume);
    }

    public void ForgetSuspendedTool() => _suspended = ToolType.None;

    // Puts one tool back the way it started on every view. Only Undo All
    // calls it, once every edit has been walked back, so it touches no view
    // that still has history to keep.
    private void ResetTool(ToolType tool)
    {
        OnToolReset?.Invoke(tool);
        if (TryEditKindOf(tool, out EditKind kind)) EditList.Active.DropKind(kind);
        SyncViews();
    }

    private static void SyncViews()
    {
        foreach (IView view in Scene.Views) view.SyncToTimeline();
    }

    private static bool TryEditKindOf(ToolType tool, out EditKind kind) =>
        Enum.TryParse(tool.ToString(), out kind);

    // An edit goes back to the view that made it, which knows how to undo it.
    private static UndoResult UndoTop(out string kindName)
    {
        Edit rec = EditList.Active.Peek();
        kindName = rec != null ? Edit.KindName(rec.kind) : null;
        if (rec == null) return UndoResult.Stale;

        IView view = Scene.ViewOf(rec.view);
        if (view == null) return UndoResult.Unreachable;

        UndoResult outcome = view.Undo(rec);
        if (outcome != UndoResult.Unreachable)
        {
            EditList.Active.Pop();
            SyncViews();
        }
        return outcome;
    }

    // Applied when the newest step was undone; Stale when its records no
    // longer matched and were dropped; Unreachable when it could not run now
    // and was kept, and trying again will not help until something changes.
    public UndoResult Undo()
    {
        if (EditList.Active.Peek() == null) return UndoResult.Stale;

        int inGroup = EditList.Active.TopGroupSize();

        switch (UndoTop(out string kindName))
        {
            case UndoResult.Applied:
                for (int i = 1; i < inGroup; i++)
                    if (UndoTop(out _) == UndoResult.Unreachable) break;
                StateChannel.Record("Undo", inGroup > 1
                    ? $"undid the {kindName} edit ({inGroup} steps, one action)"
                    : $"undid the {kindName} edit");
                return UndoResult.Applied;

            case UndoResult.Unreachable:
                Debug.LogWarning($"[ManageTools] {kindName} undo could not run; the record was kept.");
                return UndoResult.Unreachable;

            default:
                Debug.LogWarning($"[ManageTools] {kindName} undo no longer matches the scene; the record was dropped.");
                return UndoResult.Stale;
        }
    }

    public void UndoAll()
    {
        int had = EditList.Active.Count;

        for (int i = 0; i < had && EditList.Active.Peek() != null; i++)
            if (UndoTop(out _) == UndoResult.Unreachable) break;

        foreach (ToolType tool in Enum.GetValues(typeof(ToolType)))
            if (tool != ToolType.None) ResetTool(tool);

        EditList.Active.Clear();
        SyncViews();
        if (had > 0) StateChannel.Record("Undo", $"undid all {had} edits");
    }
}
