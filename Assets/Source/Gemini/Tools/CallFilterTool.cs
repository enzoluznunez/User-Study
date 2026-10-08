using System.Collections.Generic;
using System.Linq;
using Google.GenAI.Types;

public sealed class CallFilterTool : AgenticTool<CallFilterTool.Args> {

    public class Args {
        [Doc("Which view to filter, when both the graph and the sheet are in the room."), Values("graph", "sheet"), Optional]
        public string view;
        [Doc("Sheet only: which axis, 'company' for the rows or 'metric' for the columns. Defaults to 'metric'. " +
             "One call filters one axis; send a second call for the other."), Optional]
        public string axis;
        [Doc("Things to switch off: on the graph investors (filers) or holdings (securities) by name, CIK or " +
             "CUSIP; on the sheet rows or columns by name or by 1-based position among those showing."), Optional]
        public string[] hide;
        [Doc("Things to switch back on, by name; hidden ones have no position."), Optional]
        public string[] show;
        [Doc("Keep only these and take every other one off. Use this for 'just show me X'; it replaces what is " +
             "taken off rather than adding to it. On the graph, naming only investors keeps the holdings they hold, " +
             "naming only holdings keeps the investors holding them, and naming both keeps just those."), Optional]
        public string[] only;
        [Doc("Put everything back. On the graph this leaves 'minValue' alone unless you give it too."), Optional]
        public bool? showAll;
        [Doc("Graph only: hide every edge (position) smaller than this, in whole US dollars: 100000000 is 100 " +
             "million. 0 shows every size. Nodes stay; only edges go."), Limits(0, 1e15), Optional]
        public double? minValue;
    }

    protected override bool EditsAreOutcome => true;

    public override FunctionDeclaration Declaration => new FunctionDeclaration {
        Name = "CallFilterTool",
        Description = "Choose what stands in the room. On the graph: switch investors (filers) or holdings " +
                      "(securities) off and back on, keep only some, or hide edges below a dollar amount with " +
                      "'minValue'. Switching an investor off hides it and its edges; the holdings stay until they " +
                      "are switched off themselves. On the sheet: take rows or columns off and bring them back, one " +
                      "axis per call; a hidden line keeps its place in the arrangement. Everything in one call is " +
                      "one edit on the undo timeline. What is filtered off is not gone from the data: the data " +
                      "tools still read it. Something always stays showing.",
        Parameters = ParametersFor(typeof(Args))
    };

    protected override void Run(Args args, Dictionary<string, object> result) {
        bool only = args.only != null && args.only.Length > 0;
        bool edits = (args.hide?.Length ?? 0) > 0 || (args.show?.Length ?? 0) > 0 || args.showAll == true;
        if (only && edits) {
            result["error"] = "'only' already says what should stay; do not send 'hide', 'show' or 'showAll' with it.";
            return;
        }

        if (!ResolveView(args.view, result, out ViewKind view)) return;
        if (!EnsureToolSelected(ToolType.Filter, result)) return;
        result["view"] = Views.Name(view);
        if (view == ViewKind.Graph) RunGraph(args, result);
        else RunSheet(args, result);
    }

    // ----- The sheet -----

    private static void RunSheet(Args args, Dictionary<string, object> result) {
        var filter = Scene.Filter;
        if (filter == null) { result["error"] = "Filter tool not found in scene."; return; }
        if (args.minValue.HasValue) { result["error"] = "'minValue' is for the graph; the sheet filters by name."; return; }

        var data = Scene.Data;
        if (data == null || !data.IsLoaded) { result["error"] = "No dataset is open."; return; }

        bool rows;
        if (!ReadAxis(args.axis, out rows, result)) return;

        bool showAll = args.showAll ?? false;
        bool only = args.only != null && args.only.Length > 0;
        bool named = (args.hide != null && args.hide.Length > 0) || (args.show != null && args.show.Length > 0);

        if (!showAll && !only && !named) {
            NeedChoice(result, rows ? "companies" : "metrics",
                filter.Names(rows),
                $"Say which {(rows ? "companies" : "metrics")} to hide or show.");
            return;
        }

        var hidden = new HashSet<int>(showAll
            ? new List<int>()
            : rows ? data.HiddenRowsInOrder() : data.HiddenGroupsInOrder());

        if (only) {
            List<int> keep = new List<int>();
            if (!Resolve(filter, rows, args.only, result, keep)) return;

            hidden.Clear();
            List<int> all = rows ? data.DataRowsInOrder() : data.DataGroupsInOrder();
            for (int i = 0; i < all.Count; i++)
                if (!keep.Contains(all[i])) hidden.Add(all[i]);
        }
        else {
            var toHide = new List<int>();
            var toShow = new List<int>();
            if (!Resolve(filter, rows, args.hide, result, toHide)) return;
            if (!Resolve(filter, rows, args.show, result, toShow)) return;

            for (int i = 0; i < toHide.Count; i++) hidden.Add(toHide[i]);
            for (int i = 0; i < toShow.Count; i++) hidden.Remove(toShow[i]);
        }

        // A false return with nothing to say is a filter that was already in
        // force; the timeline shows that as 'changed' false and reads back below.
        bool applied = filter.Apply(rows, new List<int>(hidden), out string refusal);

        if (!applied && refusal != null) {
            result["error"] = refusal;
            return;
        }

        Report(data, rows, result);
    }

    // 'metric' unless the caller says otherwise, which is what a filter meant
    // before the tool could reach the other axis.
    private static bool ReadAxis(string axis, out bool rows, Dictionary<string, object> result) {
        rows = false;
        if (string.IsNullOrWhiteSpace(axis)) return true;

        string wanted = axis.Trim().ToLowerInvariant();
        switch (wanted) {
            case "company": case "companies": case "row": case "rows":
                rows = true;
                return true;
            case "metric": case "metrics": case "column": case "columns":
                return true;
            default:
                result["error"] = $"'{axis.Trim()}' is not an axis; say 'metric' or 'company'.";
                return false;
        }
    }

    private static bool Resolve(FilterTool filter, bool rows, string[] wanted,
        Dictionary<string, object> result, List<int> into) {

        if (wanted == null) return true;

        string noun = rows ? "company" : "metric";
        foreach (string name in wanted) {
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!filter.TryResolve(rows, name, out int id)) {
                result["error"] = $"No single {noun} matches '{name.Trim()}'.";
                result[rows ? "companies" : "metrics"] = new List<object>(filter.Names(rows));
                return false;
            }
            if (!into.Contains(id)) into.Add(id);
        }
        return true;
    }

    private static void Report(DataSource data, bool rows, Dictionary<string, object> result) {
        var showing = new List<object>();
        var off = new List<object>();

        if (rows)
            foreach (int row in data.DataRowsInOrder())
                (data.IsRowHidden(row) ? off : showing).Add(DataSource.RowLabelOfData(data, row));
        else
            foreach (int group in data.DataGroupsInOrder())
                (data.IsDataGroupHidden(group) ? off : showing).Add(DataSource.GroupLabelOfData(data, group));

        string noun = rows ? "company" : "metric";
        result["axis"] = noun;
        result["showing"] = showing;
        result["hidden"] = off;
        result["note"] = off.Count == 0
            ? $"Every {noun} is on the sheet."
            : $"Positions have shifted; the {noun}s on the sheet are the ones listed in 'showing', in that order.";
    }


    // ----- The graph -----

    private static void RunGraph(Args args, Dictionary<string, object> result) {
        if (!string.IsNullOrWhiteSpace(args.axis)) { result["error"] = "'axis' is for the sheet; on the graph just name the filers or securities."; return; }
        bool hasOnly = args.only != null && args.only.Length > 0;
        bool hasEdit = (args.hide?.Length ?? 0) > 0 || (args.show?.Length ?? 0) > 0 || args.showAll == true;
        if (!hasOnly && !hasEdit && !args.minValue.HasValue) {
            NeedChoice(result, "what to filter", null,
                "Say what to hide, show or keep, or give 'minValue' to hide small holdings.");
            return;
        }

        FilterTool filter = Scene.Filter;
        ManageGraph graph = Scene.Graph;
        if (filter == null || graph == null || graph.Data == null) { result["error"] = "The graph is not ready."; return; }

        var hidden = new HashSet<string>(args.showAll == true ? new string[0] : graph.Hidden);

        if (hasOnly) {
            if (!Resolve(args.only, result, out List<string> keep)) return;
            hidden = new HashSet<string>(Only(graph, keep));
        }
        else {
            if (!Resolve(args.hide, result, out List<string> hide)) return;
            if (!Resolve(args.show, result, out List<string> show)) return;
            hidden.UnionWith(hide);
            hidden.ExceptWith(show);
        }

        long minValue = args.minValue.HasValue ? (long)System.Math.Max(0, args.minValue.Value) : graph.MinValue;

        if (!filter.ApplyGraph(FilterState.Of(hidden, minValue), out string refusal) && refusal != null) {
            result["error"] = refusal;
            return;
        }
        ReportGraph(graph, result);
    }

    // Keeping only some nodes keeps what joins them. Holdings no longer leave
    // with their investors, so the rest are switched off by name, the same way
    // the lists would: naming investors keeps the holdings they hold, naming
    // holdings keeps the investors holding them, naming both keeps just those.
    private static IEnumerable<string> Only(ManageGraph graph, List<string> keep) {
        var kept = new HashSet<string>(keep);
        var filers = graph.DrawnFilers.Where(f => kept.Contains(f.Id)).ToList();
        var securities = graph.DrawnSecurities.Where(s => kept.Contains(s.Id)).ToList();

        var keepFilers = new HashSet<string>(filers.Select(f => f.Id));
        var keepSecurities = new HashSet<string>(securities.Select(s => s.Id));
        if (securities.Count == 0)
            foreach (Filer f in filers)
                foreach (Holding h in f.Holdings) keepSecurities.Add(h.Security.Id);
        if (filers.Count == 0)
            foreach (Security s in securities)
                foreach (Holding h in s.Holders) keepFilers.Add(h.Filer.Id);

        return graph.DrawnFilers.Where(f => !keepFilers.Contains(f.Id)).Select(f => f.Id)
            .Concat(graph.DrawnSecurities.Where(s => !keepSecurities.Contains(s.Id)).Select(s => s.Id));
    }

    private static bool Resolve(string[] names, Dictionary<string, object> result, out List<string> ids) {
        ids = new List<string>();
        if (names == null) return true;
        foreach (string name in names) {
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!TryResolveNode(name, result, out string id)) return false;
            if (!ids.Contains(id)) ids.Add(id);
        }
        return true;
    }

    private static void ReportGraph(ManageGraph graph, Dictionary<string, object> result) {
        result["investorsOn"] = graph.VisibleFilerCount;
        result["holdingsOn"] = graph.VisibleSecurityCount;
        result["edgesShowing"] = graph.VisibleHoldings.Count;
        result["switchedOff"] = graph.Hidden.Select(graph.NameOf).Where(n => n != null).Cast<object>().ToList();
        result["minValueUsd"] = graph.MinValue;
    }
}
