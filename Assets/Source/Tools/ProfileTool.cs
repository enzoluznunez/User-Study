using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Profiles one part of whichever view it is used on, and reports its count,
// range, average and total on a card beside it.
//
// On the bar sheet it raises a whole row or column as a strip above the
// sheet: press a bar, then sweep along the line you want.
//
// On the network graph it runs a breadth-first search out from the node you
// poke, as many hops as the panel says: one hop is what a filer holds or who
// holds a security, two is who else holds those, three is what they hold
// besides. Reached nodes stay lit, fainter the further out; the rest dim. Poke
// the profiled node again to let it go.
public class ProfileTool : Tool
{
    public float liftAboveMaximumHeight = 0f;

    [Tooltip("How far a pressed finger must travel, in cells, before the sweep direction chooses the axis.")]
    public float travelCells = 0.6f;

    private readonly AxisIntent _intent = new AxisIntent { deadband = 0.25f };
    private bool _pressed;
    private Vector3 _pressLocal;

    protected override ToolType Kind => ToolType.Profile;

    protected override bool UsesSheetEvents => true;
    protected override bool UsesGraphEvents => true;

    protected override void OnResetTool()
    {
        StatsTooltip.Hide();
        if (graph != null) graph.SetProfile(GraphProfile.None, out _);
    }

    protected override void OnActiveChanged(bool active)
    {
        _pressed = false;
        _intent.Reset();
        if (active) ShowGraphCard();
        else
        {
            ClearTint();
            ClearHover();
            StatsTooltip.Hide();
        }
    }

    private static bool Usable(ReadSheets.Reading reading) =>
        reading.valid && reading.cube != null && reading.sheet != null;

    private void Tint(ReadSheets.Reading reading, float swell) => TintLine(reading, _intent.Columns, swell);

    private void FeedReach(ReadSheets.Reading reading)
    {
        if (reading.tip == Vector3.zero) return;
        if (AxisIntent.ReachScores(reading.sheet, reading.wrist, reading.tip, out float forColumns, out float forRows))
            _intent.Feed(forColumns, forRows);
    }

    private void FeedSweep(ReadSheets.Reading reading)
    {
        if (reading.tip == Vector3.zero) return;

        Vector3 delta = reading.sheet.transform.InverseTransformPoint(reading.tip) - _pressLocal;
        float travel = travelCells * reading.sheet.CellSize;
        if (Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.z)) < travel) return;

        AxisIntent.SweepScores(delta, out float forColumns, out float forRows);
        _intent.Feed(forColumns, forRows);
        _intent.Latch();
    }

    protected override void OnSheetHover(ReadSheets.Reading reading)
    {
        if (!Active || sheetManager == null || !Usable(reading))
        {
            ClearTint();
            return;
        }

        if (_pressed) FeedSweep(reading);
        else FeedReach(reading);

        if (!_intent.Decided)
        {
            ClearTint();
            return;
        }

        Tint(reading, _pressed ? Style.PreviewSwell + Style.EngageSwell : Style.PreviewSwell);
    }

    protected override void OnSheetSelect(ReadSheets.Reading reading)
    {
        if (!Active || sheetManager == null || !Usable(reading)) return;

        FeedReach(reading);
        _pressed = true;
        _pressLocal = reading.sheet.transform.InverseTransformPoint(
            reading.tip == Vector3.zero ? reading.point : reading.tip);

        if (_intent.Decided) Tint(reading, Style.PreviewSwell + Style.EngageSwell);
    }

    protected override void OnSheetRelease(ReadSheets.Reading reading)
    {
        ClearTint();
        _pressed = false;
        _intent.Release();
    }

    protected override void OnSheetCleared()
    {
        ClearTint();
        _pressed = false;
        _intent.Reset();
    }

    protected override void OnSheetCommit(ReadSheets.Reading reading)
    {
        ClearTint();
        if (!Active || sheetManager == null || !Usable(reading) || !_intent.Decided) return;

        bool columns = _intent.Columns;
        if (!Project(columns, reading.cube.dataRow, reading.cube.dataCol, reading.visRow, reading.visCol)) return;

        ShowStats(columns, reading);
    }

    private void ShowStats(bool columns, ReadSheets.Reading reading)
    {
        if (!StatsTooltip.TryResolve(sheetManager, reading,
                out Tooltip tooltip, out DataSource data, out CreateSheet piece)) return;

        int line = columns ? reading.visCol : reading.visRow;
        string name = columns && data.IsGrouped(true)
            ? DataSource.GroupLabelAt(data, true, data.GroupOf(true, line))
            : data.TitleAt(columns, line);
        string title = string.IsNullOrEmpty(name) ? $"{(columns ? "Column" : "Row")} {line + 1}" : name;

        // Across a grouped axis the cells hold different metrics, so a row's
        // summary is taken over the one metric that was touched rather than over
        // every column, which would average dollars with share counts.
        int colLo = piece.colMin;
        int colHi = piece.colMax;
        if (!columns && data.IsGrouped(true))
        {
            int group = data.GroupOf(true, reading.visCol);
            data.GroupSpan(true, group, out colLo, out colHi);
            colLo = Mathf.Max(colLo, piece.colMin);
            colHi = Mathf.Min(colHi, piece.colMax);
            title += " · " + DataSource.GroupLabelAt(data, true, group);
        }

        // A column strip raises the whole metric, so its summary covers every year.
        int stripLo = line, stripHi = line;
        if (columns) sheetManager.ColumnStripSpan(piece, line, out stripLo, out stripHi);

        Tooltip.SelectionStats selection = new Tooltip.SelectionStats
        {
            title = title,
            stats = columns
                ? SheetStats.Over(data, piece.rowMin, piece.rowMax, stripLo, stripHi).AsValues()
                : SheetStats.Over(data, line, line, colLo, colHi).AsValues()
        };

        ManageSheets sheets = sheetManager;
        CreateSheet target = piece;
        float lift = liftAboveMaximumHeight;
        Vector3 fallback = reading.point;

        tooltip.ShowStats(
            () => sheets != null && sheets.TryStripTopPoint(target, columns, line, lift, out Vector3 raised)
                ? raised
                : fallback,
            selection, ViewKind.Sheet);
    }

    public bool ShowProfile(bool columns, int visRow, int visCol)
    {
        if (!Active || sheetManager == null) return false;

        CreateCube cube = sheetManager.CubeAt(visRow, visCol);
        if (cube == null) return false;

        return Project(columns, cube.dataRow, cube.dataCol, visRow, visCol);
    }

    private bool Project(bool columns, int dataRow, int dataCol, int visRow, int visCol)
    {
        ProjectionRecord rec = new ProjectionRecord
        {
            isColumn = columns,
            dataRow = dataRow,
            dataCol = dataCol,
            lift = liftAboveMaximumHeight
        };

        if (!sheetManager.PushProjection(rec, EditKind.Profile)) return false;

        DataSource data = Scene.Data;
        int line = columns ? visCol : visRow;
        string label = columns && data != null && data.IsGrouped(true)
            ? DataSource.GroupLabelAt(data, true, data.GroupOf(true, line))
            : DataSource.LabelAt(data, columns, line);
        Report($"projected {label}");
        return true;
    }

    // ----- The graph: a breadth-first search out from one node -----

    private const string HopsRowName = "HopsRow";
    private static readonly string[] HopLabels = { "1 Hop", "2 Hops", "3 Hops" };

    private ButtonList _hopsRow;
    private int _hops = 1;

    // How far the next profile reaches. Changing it re-runs the one standing.
    public int Hops => _hops;

    protected override void OnToolStart()
    {
        if (graph != null) graph.OnStateChanged += ShowGraphCard;
    }

    protected override void OnToolDestroy()
    {
        if (graph != null) graph.OnStateChanged -= ShowGraphCard;
    }

    protected override void BuildPanelContent()
    {
        if (toolPanelUI == null || !Views.Graph) return;

        var buttons = new (string, string, UnityEngine.Events.UnityAction)[HopLabels.Length];
        for (int i = 0; i < HopLabels.Length; i++)
        {
            int hops = i + 1;
            buttons[i] = ($"Hops_{hops}", HopLabels[i], () => OnHopsClicked(hops));
        }
        _hopsRow = toolPanelUI.AddToggleRow(Kind, HopsRowName, buttons);
        _hopsRow?.SetSelected(_hops - 1);
        toolPanelUI.ContentChanged();
    }

    private void OnHopsClicked(int hops)
    {
        _hops = hops;
        _hopsRow?.SetSelected(hops - 1);
        Report($"set the profile to reach {hops} hop{(hops == 1 ? "" : "s")} out");

        // A profile already standing widens or narrows to match, as one edit.
        if (graph != null && !graph.Profile.IsNone && graph.Profile.hops != hops)
            ProfileNode(graph.Profile.root, hops, out _);
    }

    protected override void OnNodeCommit(ReadGraph.Reading reading)
    {
        if (!Active || graph == null || !reading.valid) return;

        string id = reading.node.Id;
        bool again = graph.Profile.root == id;
        if (!ProfileNode(again ? null : id, _hops, out string refusal) && refusal != null)
            Notices.Show(this, "Profile", refusal);
    }

    // The one way a graph profile changes, for the hand and the assistant
    // alike: it records the edit and says what happened. A null id lets go.
    public bool ProfileNode(string id, int hops, out string refusal)
    {
        refusal = null;
        if (graph == null) { refusal = "The graph is not ready."; return false; }

        GraphProfile before = graph.Profile;
        GraphProfile after = id == null ? GraphProfile.None : new GraphProfile { root = id, hops = hops };
        if (!graph.SetProfile(after, out refusal)) return false;

        hops = graph.Profile.hops;
        if (id != null && hops != _hops)
        {
            _hops = hops;
            _hopsRow?.SetSelected(hops - 1);
        }

        EditList.Active.PushGraphProfile(before);
        Report(id == null
            ? $"let go of the profile of {graph.NameOf(before.root) ?? "the graph"}"
            : $"profiled {graph.NameOf(id)} {hops} hop{(hops == 1 ? "" : "s")} out, reaching " +
              $"{graph.ProfileHops.Count - 1} others");
        return true;
    }

    // The positions inside the profile, summed and ranged, beside its root.
    private void ShowGraphCard()
    {
        if (!Active || graph == null) return;

        GraphProfile profile = graph.Profile;
        if (profile.IsNone || graph.ProfileHops.Count == 0) { StatsTooltip.Hide(ViewKind.Graph); return; }

        Tooltip tooltip = Scene.Tooltip;
        if (tooltip == null) return;

        List<double> values = graph.ProfileHoldings().Select(h => (double)h.Value).ToList();
        int filers = graph.ProfileHops.Keys.Count(id => graph.View.NodeOf(id)?.IsFiler == true);
        int securities = graph.ProfileHops.Count - filers;
        string root = profile.root;

        tooltip.ShowStats(() => graph.TryNodeWorldPosition(root, out Vector3 at) ? at : transform.position,
            new Tooltip.SelectionStats
            {
                title = $"{GraphLabels.Shorten(graph.NameOf(root))} · {profile.hops} hop{(profile.hops == 1 ? "" : "s")}: " +
                        $"{filers} filer{(filers == 1 ? "" : "s")}, {securities} securit{(securities == 1 ? "y" : "ies")}",
                stats = ValueStats.Of(values)
            }, ViewKind.Graph);
    }
}
