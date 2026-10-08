using System.Collections.Generic;
using UnityEngine;

public enum EditKind { Move, Rotate, Scale, Sort, Filter, Profile }

// What undoing one edit came to: done, no longer matching the scene (the
// record is dropped), or not possible right now (the record is kept).
public enum UndoResult { Applied, Stale, Unreachable }

public struct MoveRecord
{
    public int sheetId;
    public Vector3 prePos;
    public Quaternion preRot;
    public Vector3 preScale;
    public float distance;

    // A grab that never changed the scale can leave it zeroed; read it as one.
    public Vector3 SafePreScale => preScale.sqrMagnitude > 1e-6f ? preScale : Vector3.one;
}

// Always a strip: one whole row or column raised above the sheet.
public struct ProjectionRecord
{
    public bool isColumn;
    public int dataRow;
    public int dataCol;
    public float lift;
}

// What the Filter tool leaves on the graph: the filers and securities taken off
// by hand, and the smallest position still drawn. Kept whole on each edit, so
// undo restores one state rather than replaying a difference.
public struct FilterState
{
    public List<string> hidden;
    public long minValue;

    public static FilterState Of(IEnumerable<string> hidden, long minValue) =>
        new FilterState { hidden = new List<string>(hidden), minValue = minValue };

    public bool SameAs(FilterState other)
    {
        if (minValue != other.minValue) return false;
        int a = hidden?.Count ?? 0, b = other.hidden?.Count ?? 0;
        if (a != b) return false;
        if (a == 0) return true;
        return new HashSet<string>(hidden).SetEquals(other.hidden);
    }
}

// What the Profile tool has picked out of the graph: a node, and how many hops
// of breadth-first search out from it are lit. No root is no profile.
public struct GraphProfile
{
    public string root;
    public int hops;

    public bool IsNone => root == null;
    public static readonly GraphProfile None = default;

    public bool SameAs(GraphProfile other) => root == other.root && (root == null || hops == other.hops);
}

// How the Sort tool has arranged the graph: where the data's layout put each
// node, or filers and securities in two columns ordered by one measure.
public enum GraphOrder { Layout, Value, Holders, Name }

public class Edit
{
    public EditKind kind;
    public ViewKind view;
    public int sheetId = -1;
    public MoveRecord move;
    public ProjectionRecord projection;

    public bool reorderIsColumn;
    public List<int> reorderPreOrder;
    public DataSource.SortMode reorderPreMode;
    public int reorderFrom;
    public int reorderTarget;
    public int reorderLines;

    public int group;

    // When it was made, counting every edit ever pushed: it does not shift when
    // older edits leave the list, as an index would.
    public long serial;

    // Which column groups were hidden before this filter, and after it. Undo puts
    // the first back; ListDatasets reports the second.
    public List<int> filterPreHidden;
    public List<int> filterPostHidden;

    // Which axis the hidden set belongs to. One edit only ever covers one axis,
    // so a press on the company list and a press on the metric list undo apart.
    public bool filterIsRow;

    // The graph's side of the same three tools: each edit keeps the state it
    // replaced, so undo sets it back whatever came between.
    public FilterState graphFilterBefore;
    public GraphProfile graphProfileBefore;
    public GraphOrder graphOrderBefore;

    public static string KindName(EditKind kind)
    {
        switch (kind)
        {
            case EditKind.Move: return "move";
            case EditKind.Rotate: return "rotate";
            case EditKind.Scale: return "scale";
            case EditKind.Sort: return "sort";
            case EditKind.Filter: return "filter";
            case EditKind.Profile: return "profile";
            default: return "edit";
        }
    }
}

public class EditList : List<Edit>
{
    // The one timeline. The graph and the sheet share it, so Undo walks every
    // edit newest first whichever tool made it and on whichever view.
    public static readonly EditList Active = new EditList();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Active.Clear();

    private int _groupSeq;
    private int _group;
    private long _serial;

    // The serial the next edit will get.
    public long NextSerial => _serial + 1;

    public int OpenGroup() => _group = ++_groupSeq;
    public void CloseGroup() => _group = 0;

    public Edit Peek() => Count > 0 ? this[Count - 1] : null;

    public int TopGroupSize()
    {
        Edit top = Peek();
        if (top == null) return 0;
        if (top.group == 0) return 1;

        int n = 0;
        for (int i = Count - 1; i >= 0 && this[i].group == top.group; i--) n++;
        return n;
    }

    public int UndoStepCount()
    {
        int steps = 0;
        int i = Count - 1;
        while (i >= 0)
        {
            int g = this[i].group;
            steps++;
            if (g == 0) { i--; continue; }
            while (i >= 0 && this[i].group == g) i--;
        }
        return steps;
    }

    public static event System.Action OnChanged;

    private static void RaiseChanged() => OnChanged?.Invoke();

    private void Push(Edit e)
    {
        e.serial = ++_serial;
        e.group = _group;
        Add(e);
        RaiseChanged();
    }

    public void DropAt(int index)
    {
        if (index < 0 || index >= Count) return;
        RemoveAt(index);
        RaiseChanged();
    }

    public int CountSince(long serial) => FindAll(e => e.serial >= serial).Count;

    // Makes every edit since 'serial' one step on the timeline.
    public int StampGroupSince(long serial)
    {
        int id = ++_groupSeq, stamped = 0;
        foreach (Edit e in this)
            if (e.serial >= serial) { e.group = id; stamped++; }
        if (stamped > 0) RaiseChanged();
        return stamped;
    }

    // Puts edits kept elsewhere back on the timeline, newest last.
    public void Restore(IEnumerable<Edit> edits)
    {
        int before = Count;
        AddRange(edits);
        if (Count != before) RaiseChanged();
    }

    public Edit Pop()
    {
        if (Count == 0) return null;
        Edit e = this[Count - 1];
        RemoveAt(Count - 1);
        RaiseChanged();
        return e;
    }

    public new void Clear()
    {
        if (Count == 0) return;
        base.Clear();
        RaiseChanged();
    }

    public void PushMove(MoveRecord move, EditKind kind, ViewKind view) =>
        Push(new Edit { kind = kind, view = view, sheetId = move.sheetId, move = move });

    public void PushSort(bool isColumn, IReadOnlyList<int> preOrder, DataSource.SortMode preMode, int from, int target) =>
        Push(new Edit
        {
            kind = EditKind.Sort,
            view = ViewKind.Sheet,
            reorderIsColumn = isColumn,
            reorderPreOrder = preOrder != null ? new List<int>(preOrder) : new List<int>(),
            reorderPreMode = preMode,
            reorderFrom = from,
            reorderTarget = target,
            reorderLines = 1
        });

    public void PushReorder(bool isColumn, IReadOnlyList<int> preOrder, DataSource.SortMode preMode, int linesMoved) =>
        Push(new Edit
        {
            kind = EditKind.Sort,
            view = ViewKind.Sheet,
            reorderIsColumn = isColumn,
            reorderPreOrder = preOrder != null ? new List<int>(preOrder) : new List<int>(),
            reorderPreMode = preMode,
            reorderFrom = -1,
            reorderTarget = -1,
            reorderLines = linesMoved
        });

    public void PushProjection(ProjectionRecord projection, EditKind kind) =>
        Push(new Edit { kind = kind, view = ViewKind.Sheet, projection = projection });

    public void PushFilter(IReadOnlyList<int> preHidden, IReadOnlyList<int> postHidden, bool isRow) =>
        Push(new Edit
        {
            kind = EditKind.Filter,
            view = ViewKind.Sheet,
            filterPreHidden = preHidden != null ? new List<int>(preHidden) : new List<int>(),
            filterPostHidden = postHidden != null ? new List<int>(postHidden) : new List<int>(),
            filterIsRow = isRow
        });

    public void PushGraphFilter(FilterState before) =>
        Push(new Edit { kind = EditKind.Filter, view = ViewKind.Graph, graphFilterBefore = before });

    public void PushGraphProfile(GraphProfile before) =>
        Push(new Edit { kind = EditKind.Profile, view = ViewKind.Graph, graphProfileBefore = before });

    public void PushGraphOrder(GraphOrder before) =>
        Push(new Edit { kind = EditKind.Sort, view = ViewKind.Graph, graphOrderBefore = before });

    // A glide stopped part way leaves a piece somewhere between where it was and
    // where it was going: the newest pose edit on that piece is corrected to
    // where it actually is, and dropped if that is where it started.
    public void AmendNewestPose(ViewKind view, int sheetId, Transform piece, Transform root)
    {
        if (piece == null) return;
        for (int i = Count - 1; i >= 0; i--)
        {
            Edit e = this[i];
            if (e.view != view || e.move.sheetId != sheetId) continue;
            if (e.kind != EditKind.Move && e.kind != EditKind.Rotate && e.kind != EditKind.Scale) continue;

            MoveRecord m = e.move;
            m.distance = root.TransformVector(piece.localPosition - m.prePos).magnitude;
            e.move = m;

            if ((piece.localPosition - m.prePos).sqrMagnitude < 1e-8f &&
                Quaternion.Angle(piece.localRotation, m.preRot) < 0.01f &&
                (piece.localScale - m.SafePreScale).sqrMagnitude < 1e-8f)
                DropAt(i);
            return;
        }
    }

    public void DropKind(EditKind kind)
    {
        if (RemoveAll(e => e.kind == kind) > 0) RaiseChanged();
    }

    // A sheet edit means nothing to a different dataset, so switching datasets
    // takes the old one's edits off the timeline; the graph's stay.
    public int DropView(ViewKind view)
    {
        int n = RemoveAll(e => e.view == view);
        if (n > 0) RaiseChanged();
        return n;
    }
}
