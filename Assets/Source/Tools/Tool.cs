using UnityEngine;

// A hand tool. Each one can act on the bar sheet, the network graph, or both,
// and listens to whichever views are in the room: a poke on a bar arrives as a
// sheet event and a poke on a node as a graph event, so the view a hand touched
// is the view the tool acts on, and nobody has to say which.
public abstract class Tool : MonoBehaviour
{
    public ManageTools toolManager;
    public ManageSheets sheetManager;
    public ReadSheets readSheets;
    public ManageGraph graph;
    public ReadGraph reader;
    public ToolPanelUI toolPanelUI;

    private bool _active;

    protected bool Active => _active;

    protected abstract ToolType Kind { get; }

    protected void Report(string what) => StateChannel.Record(Kind.ToString(), what);

    // ----- The sheet -----

    protected virtual bool UsesSheetEvents => false;

    protected virtual void OnSheetHover(ReadSheets.Reading reading) { }

    protected virtual void OnSheetSelect(ReadSheets.Reading reading) { }

    protected virtual void OnSheetRelease(ReadSheets.Reading reading) { }

    protected virtual void OnSheetCommit(ReadSheets.Reading reading) { }

    protected virtual void OnSheetCleared() { }

    protected void ClearTint()
    {
        if (sheetManager != null) sheetManager.ClearHoverTint();
    }

    // Lights the line under the finger: one row, or on a grouped column axis the
    // whole metric the column belongs to.
    protected void TintLine(ReadSheets.Reading reading, bool columns, float swell = Style.PreviewSwell)
    {
        int lo = reading.visRow, hi = reading.visRow;
        if (columns) sheetManager.ColumnStripSpan(reading.sheet, reading.visCol, out lo, out hi);
        sheetManager.SetLineTint(reading.sheet, columns ? 1 : 2, lo, hi, swell);
    }

    // ----- The graph -----

    protected virtual bool UsesGraphEvents => false;

    // Lights the node under the finger, the same for every tool that pokes nodes.
    protected virtual void OnNodeHover(ReadGraph.Reading reading)
    {
        if (!Active || graph == null || !reading.valid) { ClearHover(); return; }
        graph.SetHover(reading.node.Id);
    }

    protected virtual void OnNodeSelect(ReadGraph.Reading reading) { }

    protected virtual void OnNodeRelease(ReadGraph.Reading reading) { }

    protected virtual void OnNodeCommit(ReadGraph.Reading reading) { }

    protected virtual void OnNodeCleared() => ClearHover();

    protected void ClearHover()
    {
        if (graph != null) graph.SetHover(null);
    }

    // ----- Listening -----

    private bool _listeningSheets;
    private bool _listeningGraph;

    private void ListenSheets(bool on)
    {
        if (readSheets == null || on == _listeningSheets) return;
        _listeningSheets = on;
        if (on)
        {
            readSheets.OnHover += OnSheetHover;
            readSheets.OnSelect += OnSheetSelect;
            readSheets.OnRelease += OnSheetRelease;
            readSheets.OnCommit += OnSheetCommit;
            readSheets.OnCleared += OnSheetCleared;
        }
        else
        {
            readSheets.OnHover -= OnSheetHover;
            readSheets.OnSelect -= OnSheetSelect;
            readSheets.OnRelease -= OnSheetRelease;
            readSheets.OnCommit -= OnSheetCommit;
            readSheets.OnCleared -= OnSheetCleared;
        }
    }

    private void ListenGraph(bool on)
    {
        if (reader == null || on == _listeningGraph) return;
        _listeningGraph = on;
        if (on)
        {
            reader.OnHover += OnNodeHover;
            reader.OnSelect += OnNodeSelect;
            reader.OnRelease += OnNodeRelease;
            reader.OnCommit += OnNodeCommit;
            reader.OnCleared += OnNodeCleared;
        }
        else
        {
            reader.OnHover -= OnNodeHover;
            reader.OnSelect -= OnNodeSelect;
            reader.OnRelease -= OnNodeRelease;
            reader.OnCommit -= OnNodeCommit;
            reader.OnCleared -= OnNodeCleared;
        }
    }

    protected virtual void OnToolStart() { }

    protected virtual void OnToolDestroy() { }

    protected virtual void BuildPanelContent() { }

    protected virtual void OnActiveChanged(bool active) { }

    protected virtual void ClearToolState() { }

    protected virtual void OnResetTool() { }

    protected virtual void Start()
    {
        if (toolManager == null) toolManager = FindAnyObjectByType<ManageTools>();
        if (sheetManager == null) sheetManager = FindAnyObjectByType<ManageSheets>();
        if (readSheets == null && sheetManager != null) readSheets = sheetManager.GetComponent<ReadSheets>();
        if (readSheets == null) readSheets = FindAnyObjectByType<ReadSheets>();
        if (graph == null) graph = FindAnyObjectByType<ManageGraph>();
        if (reader == null && graph != null) reader = graph.GetComponent<ReadGraph>();
        if (reader == null) reader = FindAnyObjectByType<ReadGraph>();
        if (toolPanelUI == null) toolPanelUI = FindAnyObjectByType<ToolPanelUI>();

        OnToolStart();
        BuildPanelContent();

        if (toolManager != null)
        {
            toolManager.OnToolChanged += HandleToolChanged;
            toolManager.OnToolReset += HandleToolReset;
        }

        SetActive(toolManager != null && toolManager.SelectedTool == Kind);
    }

    protected virtual void OnDestroy()
    {
        if (toolManager != null)
        {
            toolManager.OnToolChanged -= HandleToolChanged;
            toolManager.OnToolReset -= HandleToolReset;
        }
        ListenSheets(false);
        ListenGraph(false);
        OnToolDestroy();
    }

    private void HandleToolChanged(ToolType selected) => SetActive(selected == Kind);

    private void HandleToolReset(ToolType tool)
    {
        if (tool != Kind) return;
        ClearToolState();
        OnResetTool();
    }

    private void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        if (!active) ClearToolState();
        if (UsesSheetEvents && Views.Sheet) ListenSheets(active);
        if (UsesGraphEvents && Views.Graph) ListenGraph(active);
        OnActiveChanged(active);
    }
}
