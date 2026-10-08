using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Chooses what stands in the room, on whichever view it is used on.
//
// On the bar sheet it hides companies (rows) and metrics (column groups). The
// arrangement underneath is untouched, so showing something again puts it back
// where the sort left it.
//
// On the network graph it has the same two lists: every investor (filer) and
// every holding (security) on the graph, all switched on to begin with.
// Switching an investor off hides it and its edges; the holdings it held stay
// showing for as long as they are switched on. The amounts hide edges smaller
// than a dollar value.
//
// The panel shows one list at a time. Neither view's list is open to begin
// with, which is not the same as nothing being filtered: the filter stands
// whether or not the list that sets it is on screen.
public class FilterTool : Tool
{
    protected override ToolType Kind => ToolType.Filter;

    protected override bool UsesSheetEvents => true;
    protected override bool UsesGraphEvents => true;

    public enum Pane { None, Company, Metric, Filers, Securities }

    public static readonly long[] Thresholds = { 0, 10_000_000, 50_000_000, 100_000_000, 250_000_000 };

    private static string ThresholdLabel(long value) => value == 0 ? "All" : $"${Formatter.Compact(value)}+";

    private const float ListHeight = 130f;
    private const string SheetRowName = "AxisRow";
    private const string GraphRowName = "GraphRow";
    private const string AmountRowName = "AmountRow";
    private const string ListName = "FilterList";

    private Pane _open = Pane.None;
    private Pane _openBuilt = Pane.None;
    private bool _listStale;
    private ButtonList _sheetRow;
    private ButtonList _graphRow;
    private ButtonList _amountRow;

    // One entry per name the open list shows: whether the thing it names is
    // showing, and the row that shows it, kept together so the two never fall
    // out of step whichever view the list belongs to.
    private readonly List<(System.Func<bool> showing, UIButton.Handle handle)> _rows =
        new List<(System.Func<bool>, UIButton.Handle)>();

    private static bool IsSheetPane(Pane pane) => pane == Pane.Company || pane == Pane.Metric;
    private static bool IsGraphPane(Pane pane) => pane == Pane.Filers || pane == Pane.Securities;

    private DataSource _watched;
    private DataSource _builtFrom;

    private static DataSource Data => Scene.Data;

    protected override void OnToolStart()
    {
        if (ManageDatasets.Instance != null)
            ManageDatasets.Instance.OnActiveDatasetChanged += OnDatasetChanged;
        if (graph != null)
        {
            graph.OnGraphChanged += Rebuild;
            graph.OnStateChanged += Light;
        }
    }

    protected override void OnToolDestroy()
    {
        if (ManageDatasets.Instance != null)
            ManageDatasets.Instance.OnActiveDatasetChanged -= OnDatasetChanged;
        if (graph != null)
        {
            graph.OnGraphChanged -= Rebuild;
            graph.OnStateChanged -= Light;
        }
        Watch(null);
    }

    protected override void BuildPanelContent() => Refresh();

    protected override void OnActiveChanged(bool active)
    {
        if (active) Refresh();
        else ClearHover();
    }

    // Undo All resets every tool: the filter it clears is the whole of this
    // tool's state, so every view comes back whole.
    protected override void OnResetTool()
    {
        DataSource data = Data;
        if (data != null)
        {
            bool metrics = data.ClearHiddenGroups();
            bool companies = data.ClearHiddenRows();
            if (metrics && companies) Report("put every company and every metric back on the sheet");
            else if (metrics) Report("showed every metric again");
            else if (companies) Report("showed every company again");
        }
        if (graph != null) graph.SetFilter(FilterState.Of(new string[0], 0), out _);
        Light();
    }

    private void OnDatasetChanged(int index) => Refresh();

    private void Watch(DataSource data)
    {
        if (_watched == data) return;

        if (_watched != null)
        {
            _watched.OnDataLoaded -= Refresh;
            _watched.OnOrderChanged -= Light;
        }
        _watched = data;
        if (_watched != null)
        {
            _watched.OnDataLoaded += Refresh;
            _watched.OnOrderChanged += Light;
        }
    }

    // ----- The panel -----

    private void Rebuild()
    {
        _listStale = true;
        Refresh();
    }

    // The lists follow the data; their squares follow the filter, which an undo
    // or the assistant can change without going through this tool.
    private void Refresh()
    {
        DataSource data = Views.Sheet ? Data : null;
        Watch(data);
        if (toolPanelUI == null) return;

        if (_sheetRow == null && _graphRow == null) BuildRows();

        // Two datasets can hold the same number of lines under different names,
        // so the list is rebuilt for a new source even when the counts match.
        if (_listStale || _openBuilt != _open || data != _builtFrom) BuildList(data);
        Light();
        toolPanelUI.ContentChanged();
    }

    private void BuildRows()
    {
        if (Views.Sheet)
            _sheetRow = toolPanelUI.AddToggleRow(Kind, SheetRowName,
                ("ByCompany", "By Company", () => OnPaneClicked(Pane.Company)),
                ("ByMetric", "By Metric", () => OnPaneClicked(Pane.Metric)));

        if (Views.Graph)
        {
            _graphRow = toolPanelUI.AddToggleRow(Kind, GraphRowName,
                ("Investors", "Investors", () => OnPaneClicked(Pane.Filers)),
                ("Holdings", "Holdings", () => OnPaneClicked(Pane.Securities)));

            var amounts = new (string, string, UnityEngine.Events.UnityAction)[Thresholds.Length];
            for (int i = 0; i < Thresholds.Length; i++)
            {
                long value = Thresholds[i];
                amounts[i] = ($"Amount_{i}", ThresholdLabel(value), () => OnAmountClicked(value));
            }
            _amountRow = toolPanelUI.AddToggleRow(Kind, AmountRowName, amounts);
        }
    }

    private void BuildList(DataSource data)
    {
        _openBuilt = _open;
        _builtFrom = data;
        _listStale = false;
        _rows.Clear();

        if ((IsSheetPane(_open) && (data == null || !data.IsLoaded)) ||
            (IsGraphPane(_open) && (graph == null || graph.Data == null)) ||
            _open == Pane.None)
        {
            toolPanelUI.RemoveContent(Kind, ListName);
            return;
        }

        ButtonList list = toolPanelUI.AddCheckList(Kind, ListHeight, ListName);
        if (list == null) return;

        switch (_open)
        {
            case Pane.Metric:
                foreach (int group in data.DataGroupsInOrder())
                    _rows.Add((() => !data.IsDataGroupHidden(group), list.Add($"Metric_{group}",
                        DataSource.GroupLabelOfData(data, group), () => OnLineClicked(false, group))));
                break;
            case Pane.Company:
                foreach (int row in data.DataRowsInOrder())
                    _rows.Add((() => !data.IsRowHidden(row), list.Add($"Company_{row}",
                        DataSource.RowLabelOfData(data, row), () => OnLineClicked(true, row))));
                break;
            case Pane.Filers:
                foreach (Filer f in graph.DrawnFilers.OrderBy(f => f.Name, System.StringComparer.OrdinalIgnoreCase))
                    _rows.Add((() => !graph.IsHidden(f.Id), list.Add($"Node_{f.Id}", f.Name, () => OnNodeClicked(f.Id))));
                break;
            case Pane.Securities:
                foreach (Security s in graph.DrawnSecurities.OrderBy(s => s.DisplayName, System.StringComparer.OrdinalIgnoreCase))
                    _rows.Add((() => !graph.IsHidden(s.Id), list.Add($"Node_{s.Id}", s.DisplayName, () => OnNodeClicked(s.Id))));
                break;
        }
    }

    // Pressing the open list closes it again, which leaves the filter exactly as
    // it was: the list is a way of setting the filter, not the filter itself.
    private void OnPaneClicked(Pane pane)
    {
        _open = _open == pane ? Pane.None : pane;
        ClearTint();
        Refresh();
        Report(_open == Pane.None ? "closed the filter list" : $"opened the list of {PaneNoun(_open)}");
    }

    private static string PaneNoun(Pane pane)
    {
        switch (pane)
        {
            case Pane.Company: return "companies";
            case Pane.Metric: return "metrics";
            case Pane.Filers: return "investors";
            default: return "holdings";
        }
    }

    // A square is filled while the thing it names is showing, so a list reads
    // as what the view is showing rather than what has been taken off it.
    private void Light()
    {
        if (_sheetRow != null)
        {
            UIButton.SetSelected(_sheetRow.At(0), _open == Pane.Company);
            UIButton.SetSelected(_sheetRow.At(1), _open == Pane.Metric);
        }
        if (_graphRow != null)
        {
            UIButton.SetSelected(_graphRow.At(0), _open == Pane.Filers);
            UIButton.SetSelected(_graphRow.At(1), _open == Pane.Securities);
        }
        if (_amountRow != null && graph != null)
            for (int i = 0; i < Thresholds.Length; i++)
                UIButton.SetSelected(_amountRow.At(i), graph.MinValue == Thresholds[i]);

        foreach ((System.Func<bool> showing, UIButton.Handle handle) in _rows)
            UIButton.SetChecked(handle, showing());
    }

    // ----- Poking the sheet -----

    // The line the finger is over, lit the way the Profile tool lights the line
    // it is about to raise, so a poke says what it will take before it takes it.
    protected override void OnSheetHover(ReadSheets.Reading reading)
    {
        if (!Active || sheetManager == null || !IsSheetPane(_open) || !reading.valid)
        {
            ClearTint();
            return;
        }
        TintLine(reading, _open == Pane.Metric);
    }

    protected override void OnSheetCleared() => ClearTint();

    // Leaving the tool leaves nothing lit behind it.
    protected override void ClearToolState()
    {
        ClearTint();
        ClearHover();
    }

    protected override void OnSheetRelease(ReadSheets.Reading reading) => ClearTint();

    protected override void OnSheetCommit(ReadSheets.Reading reading)
    {
        ClearTint();
        if (!Active || !reading.valid) return;

        // Without an axis open a poke has no line to mean, so it says so once
        // rather than guessing at the metric and taking the wrong thing off.
        if (!IsSheetPane(_open))
        {
            Notices.Show(this, "Filter",
                "Open By Company or By Metric to choose what a poke takes off the sheet.");
            return;
        }

        DataSource data = Data;
        if (data == null || !data.IsLoaded) return;

        string refusal;
        bool done = _open == Pane.Metric
            ? Toggle(false, data.DataGroupOf(reading.dataCol), out refusal)
            : Toggle(true, reading.dataRow, out refusal);

        if (!done && refusal != null) Notices.Show(this, "Filter", refusal);
    }

    // ----- Poking the graph -----

    // A node needs no list open: one poke names one investor or holding.
    protected override void OnNodeCommit(ReadGraph.Reading reading)
    {
        ClearHover();
        if (!Active || !reading.valid) return;
        if (!ToggleNode(reading.node.Id, out string refusal) && refusal != null)
            Notices.Show(this, "Filter", refusal);
    }

    // ----- The sheet's filter -----

    // A company is one row and a metric one column group; past that the two
    // axes filter the same way, so everything below takes the axis as 'rows'.
    private static List<int> InOrder(DataSource data, bool rows) =>
        rows ? data.DataRowsInOrder() : data.DataGroupsInOrder();

    private static List<int> HiddenOf(DataSource data, bool rows) =>
        rows ? data.HiddenRowsInOrder() : data.HiddenGroupsInOrder();

    private static bool IsHidden(DataSource data, bool rows, int id) =>
        rows ? data.IsRowHidden(id) : data.IsDataGroupHidden(id);

    private void OnLineClicked(bool rows, int id)
    {
        if (!Toggle(rows, id, out string refusal) && refusal != null)
            Notices.Show(this, "Filter", refusal);
    }

    public bool Toggle(bool rows, int id, out string refusal)
    {
        refusal = null;

        DataSource data = Data;
        if (data == null || !data.IsLoaded) { refusal = "No dataset is open."; return false; }

        List<int> hidden = HiddenOf(data, rows);
        if (IsHidden(data, rows, id)) hidden.Remove(id);
        else hidden.Add(id);

        return Apply(rows, hidden, out refusal);
    }

    // The whole hidden set at once: one edit on the timeline however many lines
    // it covers, so undo puts the sheet back the way one action found it.
    public bool Apply(bool rows, IReadOnlyList<int> hidden, out string refusal)
    {
        refusal = null;

        DataSource data = Data;
        if (data == null || !data.IsLoaded) { refusal = "No dataset is open."; return false; }

        List<int> before = HiddenOf(data, rows);
        bool changed = rows
            ? data.SetHiddenRows(hidden, out refusal)
            : data.SetHiddenGroups(hidden, out refusal);
        if (!changed) return false;

        List<int> after = HiddenOf(data, rows);
        EditList.Active.PushFilter(before, after, rows);

        Light();
        Report(DescribeSheet(data, before, after, rows));
        return true;
    }

    private static string DescribeSheet(DataSource data, List<int> before, List<int> after, bool rows)
    {
        var gone = new List<string>();
        var back = new List<string>();

        for (int i = 0; i < after.Count; i++)
            if (!before.Contains(after[i])) gone.Add(Label(data, after[i], rows));
        for (int i = 0; i < before.Count; i++)
            if (!after.Contains(before[i])) back.Add(Label(data, before[i], rows));

        string noun = rows ? DataSource.RowNoun(data) : DataSource.GroupNoun(data, true);
        var parts = new List<string>(2);
        if (gone.Count > 0) parts.Add($"took {Summarize(gone, noun)} off the sheet");
        if (back.Count > 0) parts.Add($"put {Summarize(back, noun)} back on the sheet");

        string what = parts.Count > 0
            ? string.Join(" and ", parts)
            : $"left the same {noun}s on the sheet";

        int showing = rows ? data.RowOrder.Count : data.GroupCount(true);
        int total = rows ? data.RowCount : data.DataGroupCount;
        return $"{what}, leaving {showing} of {total} showing";
    }

    private static string Label(DataSource data, int index, bool rows) =>
        rows ? DataSource.RowLabelOfData(data, index) : DataSource.GroupLabelOfData(data, index);

    private static string Summarize(List<string> names, string noun)
    {
        if (names.Count == 1) return names[0];
        if (names.Count <= 3) return string.Join(", ", names);
        return $"{names.Count} {noun}s";
    }

    // A company or metric by the name the user says, or by its 1-based place
    // among those on the sheet. Hidden ones answer to their names: they are what
    // the tool exists to bring back. Numbers count only what is showing, the way
    // DescribeSheet and every other tool count them, so a hidden one has none.
    public bool TryResolve(bool rows, string query, out int id)
    {
        id = -1;

        DataSource data = Data;
        if (data == null || !data.IsLoaded || string.IsNullOrWhiteSpace(query)) return false;

        string wanted = query.Trim();
        List<int> all = InOrder(data, rows);

        for (int i = 0; i < all.Count; i++)
            if (string.Equals(Label(data, all[i], rows), wanted, System.StringComparison.OrdinalIgnoreCase))
            {
                id = all[i];
                return true;
            }

        int only = -1, hits = 0;
        for (int i = 0; i < all.Count; i++)
        {
            if (Label(data, all[i], rows).IndexOf(wanted, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            only = all[i];
            hits++;
        }
        if (hits == 1) { id = only; return true; }
        if (hits > 1) return false;

        if (!int.TryParse(wanted, out int position) || position < 1) return false;
        for (int i = 0; i < all.Count; i++)
        {
            if (IsHidden(data, rows, all[i])) continue;
            if (--position == 0) { id = all[i]; return true; }
        }
        return false;
    }

    public List<string> Names(bool rows)
    {
        DataSource data = Data;
        var names = new List<string>();
        if (data == null || !data.IsLoaded) return names;

        foreach (int id in InOrder(data, rows)) names.Add(Label(data, id, rows));
        return names;
    }

    // ----- The graph's filter -----

    private void OnNodeClicked(string id)
    {
        if (!ToggleNode(id, out string refusal) && refusal != null) Notices.Show(this, "Filter", refusal);
    }

    private void OnAmountClicked(long value)
    {
        if (graph == null) return;
        if (!ApplyGraph(FilterState.Of(graph.Hidden, value), out string refusal) && refusal != null)
            Notices.Show(this, "Filter", refusal);
        Light();
    }

    public bool ToggleNode(string id, out string refusal)
    {
        refusal = null;
        if (graph == null) { refusal = "The graph is not ready."; return false; }

        var hidden = new HashSet<string>(graph.Hidden);
        if (!hidden.Remove(id)) hidden.Add(id);
        return ApplyGraph(FilterState.Of(hidden, graph.MinValue), out refusal);
    }

    // The whole graph filter at once: one edit on the timeline however much it
    // changes, so Undo puts the graph back the way one action found it.
    public bool ApplyGraph(FilterState state, out string refusal)
    {
        refusal = null;
        if (graph == null) { refusal = "The graph is not ready."; return false; }

        FilterState before = graph.CurrentFilter;
        if (!graph.SetFilter(state, out refusal)) return false;

        EditList.Active.PushGraphFilter(before);
        Light();
        Report(DescribeGraph(before, graph.CurrentFilter));
        return true;
    }

    private string DescribeGraph(FilterState before, FilterState after)
    {
        var gone = after.hidden.Where(id => !before.hidden.Contains(id)).Select(graph.NameOf).ToList();
        var back = before.hidden.Where(id => !after.hidden.Contains(id)).Select(graph.NameOf).ToList();

        var parts = new List<string>(3);
        if (gone.Count > 0) parts.Add($"switched off {Summarize(gone, "node")}");
        if (back.Count > 0) parts.Add($"switched {Summarize(back, "node")} back on");
        if (after.minValue != before.minValue)
            parts.Add(after.minValue == 0
                ? "showed edges of every size"
                : $"hid edges under {Formatter.Compact(after.minValue)} dollars");

        string what = parts.Count > 0 ? string.Join(" and ", parts) : "left the graph as it was";
        return $"{what}, leaving {graph.VisibleFilerCount} of {graph.DrawnFilers.Count} investors and {graph.VisibleSecurityCount} of " +
               $"{graph.DrawnSecurities.Count} holdings switched on";
    }
}
