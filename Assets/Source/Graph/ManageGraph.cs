using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Oculus.Interaction;

// Owns the holdings network in the room: builds it once the data has loaded,
// keeps what the tools have done to it (what is filtered off, how it is sorted,
// which node is profiled and how far out), turns that into what GraphView
// draws, and places and moves the graph in the room.
//
// Everything that changes the graph comes through here, so the hands and the
// assistant can never leave it in two different states.
public class ManageGraph : MonoBehaviour, IView
{
    [Tooltip("The grabbable piece the graph is drawn on: GraphView plus the Interaction SDK grab components.")]
    public GameObject piecePrefab;
    [Tooltip("Unlit vertex-colour material for nodes and edges.")]
    public Material material;

    [Header("Layout")]
    [Tooltip("Radius of the layout, in metres: the CSV's x, y and z run from -1 to 1 and are scaled to this.")]
    [Range(0.05f, 1.5f)] public float layoutRadius = 0.3f;
    [Tooltip("Node radius for the smallest and the largest value, in metres.")]
    public float minNodeRadius = 0.008f;
    public float maxNodeRadius = 0.026f;
    [Tooltip("Edge width for the smallest and the largest position, in metres.")]
    public float minEdgeWidth = 0.0012f;
    public float maxEdgeWidth = 0.006f;

    [Header("How much is drawn")]
    [Tooltip("Most filers drawn, largest portfolios first. The assistant still reads every one.")]
    public int maxFilersDrawn = 60;
    [Tooltip("Most securities drawn, most widely held first. The assistant still reads every one.")]
    public int maxSecuritiesDrawn = 150;

    [Header("Labels")]
    [Tooltip("A security is named without being focused or hovered when at least this many filers hold it.")]
    public int labelMinHolders = 2;
    [Tooltip("Size of the names above nodes, against the panel's text size.")]
    [Range(0.05f, 1f)] public float labelTextScale = 0.18f;

    [Header("Placement")]
    public float minimumZOffsetFromCamera = 0.35f;
    [Tooltip("Height of the graph's centre as a fraction of the viewer's eye height.")]
    [Range(0.3f, 1.1f)] public float heightFraction = 0.8f;

    [Header("System motion")]
    [Tooltip("Speed of the fastest-moving visible point in any system motion, metres per second.")]
    public float systemMotionSpeed = 0.5f;
    [Tooltip("Shortest system motion, seconds.")]
    public float minMotionSeconds = 0.1f;
    [Tooltip("Longest system motion, seconds; a large motion moves faster than the shared speed instead of running longer.")]
    public float maxMotionSeconds = 1.5f;

    public static readonly Color FilerColor = new Color(0.96f, 0.63f, 0.22f);
    public static readonly Color SecurityColor = new Color(0.27f, 0.67f, 0.88f);
    private static readonly Color Shadow = new Color(0.10f, 0.11f, 0.13f);
    private static readonly Color EdgeColor = new Color(0.60f, 0.64f, 0.70f);
    private static readonly Color EdgeLit = new Color(0.97f, 0.97f, 0.97f);
    private static readonly Color EdgeDim = new Color(0.20f, 0.21f, 0.24f);

    private const float RootSwell = 0.45f;
    private const float HoverSwell = 0.25f;

    // How far a node at each hop of a profile fades toward the shadow: the root
    // and its neighbours stay full, each hop further out a little dimmer, and
    // anything the search did not reach dimmest of all.
    private static readonly float[] HopFade = { 0f, 0f, 0.3f, 0.5f };
    private const float Unreached = 0.8f;

    [Header("Sorted arrangement")]
    [Tooltip("Most nodes stacked in one column before the next column starts.")]
    public int nodesPerColumn = 24;
    [Tooltip("Distance between neighbouring columns of a sorted arrangement, in metres.")]
    public float columnSpacing = 0.07f;
    [Tooltip("Seconds a change of arrangement takes to glide into place.")]
    public float arrangeSeconds = 0.6f;

    [Header("Beside the sheet")]
    [Tooltip("When the bar sheet is in the room too, how far right of it the graph stands, in metres.")]
    public float besideSheetOffset = 0.7f;

    public event Action OnGraphChanged;
    public event Action OnStateChanged;
    public event Action<PieceMove> MoveCommitted;

    private HoldingsData _data;
    private GraphView _view;
    private Transform _root;

    private readonly List<Filer> _drawnFilers = new List<Filer>();
    private readonly List<Security> _drawnSecurities = new List<Security>();
    private readonly List<Holding> _drawnHoldings = new List<Holding>();
    private readonly HashSet<string> _drawn = new HashSet<string>();

    private readonly HashSet<string> _hidden = new HashSet<string>();
    private long _minValue;
    private GraphProfile _profile;
    private GraphOrder _order = GraphOrder.Layout;
    private string _hover;

    // Where each node stands in the data's own layout, and where it is going.
    private readonly Dictionary<string, Vector3> _layout = new Dictionary<string, Vector3>();
    private readonly Dictionary<string, float> _radius = new Dictionary<string, float>();
    private readonly Dictionary<string, int> _hops = new Dictionary<string, int>();
    private Coroutine _arranging;

    private readonly List<Holding> _visibleHoldings = new List<Holding>();
    private readonly HashSet<string> _visible = new HashSet<string>();
    private long _maxPosition = 1;

    private bool _placementPending = true;
    private bool _anchored;
    private Vector3 _anchorCenter;
    private Quaternion _anchorYaw = Quaternion.identity;
    private bool _recenterHooked;
    private bool _grabbable;

    public HoldingsData Data => _data;
    public GraphView View => _view;
    public bool IsBuilt => _view != null && _view.IsBuilt;

    public IReadOnlyList<Filer> DrawnFilers => _drawnFilers;
    public IReadOnlyList<Security> DrawnSecurities => _drawnSecurities;
    public IReadOnlyList<Holding> VisibleHoldings => _visibleHoldings;
    public IReadOnlyCollection<string> Hidden => _hidden;
    public long MinValue => _minValue;
    public GraphProfile Profile => _profile;
    public GraphOrder Order => _order;
    public FilterState CurrentFilter => FilterState.Of(_hidden, _minValue);

    // How many filers and securities are switched on, counted with the rest of
    // what is showing whenever that changes.
    public int VisibleFilerCount { get; private set; }
    public int VisibleSecurityCount { get; private set; }

    // How many hops out from the profiled node each reached node is; empty when
    // nothing is profiled or the root is filtered off.
    public IReadOnlyDictionary<string, int> ProfileHops => _hops;

    public bool IsDrawn(string id) => id != null && _drawn.Contains(id);
    public bool IsHidden(string id) => id != null && _hidden.Contains(id);
    public bool IsVisible(string id) => id != null && _visible.Contains(id);

    // ----- Lifecycle -----

    private void OnEnable()
    {
        HoldingsData.OnLoaded += OnDataLoaded;
        OVRManager.HMDMounted += RecenterToUser;
        if (HoldingsData.Current != null && _data != HoldingsData.Current) OnDataLoaded();
    }

    private void OnDisable()
    {
        HoldingsData.OnLoaded -= OnDataLoaded;
        OVRManager.HMDMounted -= RecenterToUser;
        if (_recenterHooked && OVRManager.display != null)
        {
            OVRManager.display.RecenteredPose -= RecenterToUser;
            _recenterHooked = false;
        }
    }

    private void OnDataLoaded()
    {
        if (!Views.Graph) return;

        _data = HoldingsData.Current;
        _hidden.Clear();
        _minValue = 0;
        _profile = GraphProfile.None;
        _order = GraphOrder.Layout;
        _hover = null;
        EditList.Active.DropView(ViewKind.Graph);
        Build();
    }

    private void Update()
    {
        if (!_recenterHooked && OVRManager.display != null)
        {
            OVRManager.display.RecenteredPose += RecenterToUser;
            _recenterHooked = true;
        }

        if (_placementPending && ApplyPlacement()) _placementPending = false;

        if (_view == null) return;
        bool held = _view.IsGrabbed && !ScaleArmed;
        if (!held) _view.SetGrabLook(false);
        if (_view.PollGrabRelease(out Vector3 pos, out Quaternion rot, out Vector3 scale))
            NotifyMoveCommitted(new PiecePose(pos, rot, scale));
        if (held) _view.SetGrabLook(true);
    }

    private static bool ScaleArmed =>
        Scene.Tools != null && Scene.Tools.SelectedTool == ToolType.Scale;

    // ----- Building -----

    private void Build()
    {
        if (_data == null) return;

        SelectDrawn();
        EnsureView();

        long maxFiler = Math.Max(1, _drawnFilers.Count > 0 ? _drawnFilers.Max(f => f.ReportedPortfolioValue) : 1);
        long maxSecurity = Math.Max(1, _drawnSecurities.Count > 0 ? _drawnSecurities.Max(s => s.SampleValue) : 1);
        bool laidOut = _drawnFilers.Any(f => f.Layout.sqrMagnitude > 1e-8f) ||
                       _drawnSecurities.Any(s => s.Layout.sqrMagnitude > 1e-8f);

        _layout.Clear();
        _radius.Clear();
        int total = _drawnFilers.Count + _drawnSecurities.Count, at = 0;
        foreach (Filer f in _drawnFilers)
        {
            _layout[f.Id] = Position(f.Layout, laidOut, at++, total);
            _radius[f.Id] = Size(f.ReportedPortfolioValue, maxFiler);
        }
        foreach (Security s in _drawnSecurities)
        {
            _layout[s.Id] = Position(s.Layout, laidOut, at++, total);
            _radius[s.Id] = Size(s.SampleValue, maxSecurity);
        }

        Recompute();
        Dictionary<string, Vector3> places = Places(_order);

        var nodes = new List<(Filer, Security, Vector3, float)>();
        foreach (Filer f in _drawnFilers) nodes.Add((f, null, places[f.Id], _radius[f.Id]));
        foreach (Security s in _drawnSecurities) nodes.Add((null, s, places[s.Id], _radius[s.Id]));

        _view.Build(material, nodes);
        _view.SetGrabbable(_grabbable);
        _view.PlayGrow(GrowDuration());

        Redraw();

        _placementPending = !ApplyPlacement();
        OnGraphChanged?.Invoke();
        Debug.Log($"[ManageGraph] Drawing {_drawnFilers.Count} filers, {_drawnSecurities.Count} securities, " +
                  $"{_drawnHoldings.Count} holdings.");
    }

    // A full quarter of filings is far more than a room can hold, so the graph
    // draws the largest filers and the most widely held securities. The
    // assistant's reads are not limited by this; only the picture is.
    private void SelectDrawn()
    {
        _drawnFilers.Clear();
        _drawnSecurities.Clear();
        _drawnHoldings.Clear();
        _drawn.Clear();

        _drawnFilers.AddRange(_data.Filers
            .OrderByDescending(f => f.ReportedPortfolioValue).ThenBy(f => f.Id)
            .Take(Math.Max(1, maxFilersDrawn)));
        var filers = new HashSet<Filer>(_drawnFilers);

        _drawnSecurities.AddRange(_data.Securities
            .Where(s => s.Holders.Any(h => filers.Contains(h.Filer)))
            .OrderByDescending(s => s.Holders.Count).ThenByDescending(s => s.SampleValue).ThenBy(s => s.Id)
            .Take(Math.Max(1, maxSecuritiesDrawn)));
        var securities = new HashSet<Security>(_drawnSecurities);

        foreach (Holding h in _data.Holdings)
            if (filers.Contains(h.Filer) && securities.Contains(h.Security)) _drawnHoldings.Add(h);

        foreach (Filer f in _drawnFilers) _drawn.Add(f.Id);
        foreach (Security s in _drawnSecurities) _drawn.Add(s.Id);

        _maxPosition = Math.Max(1, _drawnHoldings.Count > 0 ? _drawnHoldings.Max(h => h.Value) : 1);
    }

    private Vector3 Position(Vector3 layout, bool laidOut, int index, int total)
    {
        if (laidOut) return layout * layoutRadius;

        // No layout in the file: spread the nodes evenly over a sphere.
        float y = 1f - 2f * (index + 0.5f) / Mathf.Max(total, 1);
        float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
        float theta = index * Mathf.PI * (3f - Mathf.Sqrt(5f));
        return new Vector3(r * Mathf.Cos(theta), y, r * Mathf.Sin(theta)) * layoutRadius;
    }

    // Area, not radius, follows value, so a node twice the value looks about
    // twice the size rather than four times.
    private float Size(long value, long max) =>
        Mathf.Lerp(minNodeRadius, maxNodeRadius, Mathf.Sqrt(Mathf.Clamp01((float)value / max)));

    private void EnsureView()
    {
        if (_root == null)
        {
            _root = new GameObject("Graph").transform;
            _root.SetParent(transform, false);
        }
        if (_view != null) return;

        GameObject go = piecePrefab != null ? Instantiate(piecePrefab, _root) : new GameObject("GraphPiece");
        if (go.transform.parent != _root) go.transform.SetParent(_root, false);
        go.name = "GraphPiece";
        _view = go.GetComponent<GraphView>();
        if (_view == null) _view = go.AddComponent<GraphView>();
    }

    // ----- State: what is showing -----

    // A node shows until it is switched off, whatever is joined to it: turning
    // an investor off takes that investor and its edges away, and the holdings
    // it held stay where they are, switched on. An edge shows while both of its
    // ends do and it is at least the smallest value still shown.
    private void Recompute()
    {
        _visibleHoldings.Clear();
        _visible.Clear();
        _visibleVersion++;
        foreach (string id in _drawn)
            if (!_hidden.Contains(id)) _visible.Add(id);
        VisibleFilerCount = _drawnFilers.Count(f => _visible.Contains(f.Id));
        VisibleSecurityCount = _visible.Count - VisibleFilerCount;

        foreach (Holding h in _drawnHoldings)
            if (_visible.Contains(h.Filer.Id) && _visible.Contains(h.Security.Id) && h.Value >= _minValue)
                _visibleHoldings.Add(h);
    }

    // Refuses, rather than clamps, a filter that would leave nothing: an empty
    // room is not a filter anyone can see their way out of.
    public bool SetFilter(FilterState state, out string refusal)
    {
        refusal = null;
        if (_data == null) { refusal = "The holdings have not loaded yet."; return false; }
        if (state.SameAs(CurrentFilter)) return false;

        var hidden = new HashSet<string>(state.hidden ?? new List<string>());
        if (_drawn.All(hidden.Contains))
        {
            refusal = "That would leave nothing on the graph; at least one node has to stay.";
            return false;
        }

        ApplyFilter(state);
        return true;
    }

    private void ApplyFilter(FilterState state)
    {
        _hidden.Clear();
        if (state.hidden != null) _hidden.UnionWith(state.hidden);
        _minValue = Math.Max(0, state.minValue);
        Recompute();

        // A sorted arrangement closes up over what is showing, so the columns
        // re-stack when the filter changes rather than keeping gaps.
        if (_order != GraphOrder.Layout) Arrange(animate: false);
        Redraw();
        OnStateChanged?.Invoke();
    }

    // ----- Profile: a breadth-first search out from one node -----

    public const int MaxHops = 3;

    public bool SetProfile(GraphProfile profile, out string refusal)
    {
        refusal = null;
        if (!profile.IsNone)
        {
            if (!IsVisible(profile.root))
            {
                refusal = IsDrawn(profile.root) ? "That node is filtered off the graph; bring it back first."
                                                : "That node is not drawn on the graph.";
                return false;
            }
            profile.hops = Mathf.Clamp(profile.hops, 1, MaxHops);
        }
        if (profile.SameAs(_profile)) return false;

        _profile = profile;
        Redraw();
        OnStateChanged?.Invoke();
        return true;
    }

    // The search reruns only when the profile or what is showing has changed.
    private GraphProfile _searched;
    private int _searchedVersion = -1;
    private int _visibleVersion;

    private void SearchProfile()
    {
        if (_searchedVersion == _visibleVersion && _searched.SameAs(_profile)) return;
        _searched = _profile;
        _searchedVersion = _visibleVersion;
        _hops.Clear();
        if (_profile.IsNone || !IsVisible(_profile.root)) return;
        foreach (var kv in GraphSearch.Hops(_visibleHoldings, _profile.root, _profile.hops)) _hops[kv.Key] = kv.Value;
    }

    // The holdings inside a profile: both ends reached, one hop apart.
    public IEnumerable<Holding> ProfileHoldings() =>
        _visibleHoldings.Where(h => _hops.TryGetValue(h.Filer.Id, out int a) &&
                                    _hops.TryGetValue(h.Security.Id, out int b) && Math.Abs(a - b) == 1);

    // ----- Sort: arranging the nodes -----

    public bool SetOrder(GraphOrder order)
    {
        if (order == _order || _view == null) return false;
        _order = order;
        Arrange(animate: true);
        OnStateChanged?.Invoke();
        return true;
    }

    // Where every node stands under an arrangement. 'Layout' is the data's own
    // x, y and z; the others stand filers in columns on the left and securities
    // on the right, ordered top to bottom, so a rank reads straight down.
    private Dictionary<string, Vector3> Places(GraphOrder order)
    {
        var places = new Dictionary<string, Vector3>(_layout);
        if (order == GraphOrder.Layout) return places;

        List<Filer> filers = _drawnFilers.Where(f => _visible.Contains(f.Id)).ToList();
        List<Security> securities = _drawnSecurities.Where(s => _visible.Contains(s.Id)).ToList();

        switch (order)
        {
            case GraphOrder.Value:
                filers = filers.OrderByDescending(f => f.ReportedPortfolioValue).ToList();
                securities = securities.OrderByDescending(s => s.SampleValue).ToList();
                break;
            case GraphOrder.Holders:
                filers = filers.OrderByDescending(f => f.Holdings.Count).ThenByDescending(f => f.ReportedPortfolioValue).ToList();
                securities = securities.OrderByDescending(s => s.Holders.Count).ThenByDescending(s => s.SampleValue).ToList();
                break;
            case GraphOrder.Name:
                filers = filers.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
                securities = securities.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
                break;
        }

        Stack(filers.Select(f => f.Id).ToList(), -1f, places);
        Stack(securities.Select(s => s.Id).ToList(), 1f, places);
        return places;
    }

    // One side's nodes in columns of at most nodesPerColumn, the first column
    // nearest the middle and later ones further out, top to bottom in rank.
    private void Stack(List<string> ids, float side, Dictionary<string, Vector3> places)
    {
        int per = Math.Max(1, nodesPerColumn);
        int rows = Math.Min(per, Math.Max(1, ids.Count));
        float height = 2f * layoutRadius;
        float step = rows > 1 ? height / (rows - 1) : 0f;

        for (int i = 0; i < ids.Count; i++)
        {
            int column = i / per, row = i % per;
            float x = side * (columnSpacing * 1.5f + column * columnSpacing);
            float y = layoutRadius - row * step;
            places[ids[i]] = new Vector3(x, rows > 1 ? y : 0f, 0f);
        }
    }

    private void Arrange(bool animate)
    {
        if (_view == null || !_view.IsBuilt) return;
        Dictionary<string, Vector3> places = Places(_order);

        if (_arranging != null) { StopCoroutine(_arranging); _arranging = null; }
        if (!animate || !isActiveAndEnabled || AgentInstant)
        {
            foreach (GraphNode node in _view.Nodes)
                if (places.TryGetValue(node.Id, out Vector3 to)) node.Place(to, _radius[node.Id]);
            _view.RefitBounds();
            return;
        }
        _arranging = StartCoroutine(ArrangeRoutine(places));
    }

    private IEnumerator ArrangeRoutine(Dictionary<string, Vector3> places)
    {
        var from = new Dictionary<GraphNode, Vector3>();
        foreach (GraphNode node in _view.Nodes) from[node] = node.transform.localPosition;

        float duration = Mathf.Max(arrangeSeconds, 0.01f), t = 0f;
        while (t < duration)
        {
            yield return null;
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
            foreach (var kv in from)
                if (kv.Key != null && places.TryGetValue(kv.Key.Id, out Vector3 to))
                    kv.Key.Place(Vector3.Lerp(kv.Value, to, k), _radius[kv.Key.Id]);
            // Only positions move mid-glide; the edges' looks are unchanged.
            _view.DrawEdges(_edgeLooks);
        }
        Redraw();
        _view.RefitBounds();
        _arranging = null;
    }

    public void CompleteArrange()
    {
        if (_arranging == null) return;
        StopCoroutine(_arranging);
        _arranging = null;
        Arrange(animate: false);
        Redraw();
    }

    // A hover lights a node without being an edit: it is where the finger is,
    // not something done to the graph.
    public void SetHover(string id)
    {
        if (_hover == id) return;
        _hover = id;
        Redraw(edges: false);
    }

    // ----- Drawing the state -----

    private readonly List<GraphView.EdgeLook> _edgeLooks = new List<GraphView.EdgeLook>();

    // A hover changes only node swell and labels, so it leaves the edge mesh
    // and the state line alone.
    private void Redraw(bool edges = true)
    {
        if (_view == null || !_view.IsBuilt) return;

        SearchProfile();
        bool profiled = _hops.Count > 0;
        string root = profiled ? _profile.root : null;

        var labelled = new List<GraphNode>();
        var strong = new HashSet<GraphNode>();

        foreach (GraphNode node in _view.Nodes)
        {
            bool visible = _visible.Contains(node.Id);
            node.SetVisible(visible);
            if (!visible) continue;

            Color hue = node.IsFiler ? FilerColor : SecurityColor;
            bool reached = _hops.TryGetValue(node.Id, out int hop);
            float fade = !profiled ? 0f : reached ? HopFade[Mathf.Min(hop, HopFade.Length - 1)] : Unreached;
            node.SetColor(Color.Lerp(hue, Shadow, fade));

            node.SetSwell(node.Id == root ? RootSwell : node.Id == _hover ? HoverSwell : 0f);

            bool named = profiled
                ? reached
                : node.IsFiler || node.Security.Holders.Count >= labelMinHolders;
            if (node.Id == _hover) named = true;
            if (named) labelled.Add(node);
            if (node.Id == root || node.Id == _hover) strong.Add(node);
        }

        if (!edges)
        {
            _view.ShowLabels(labelled, strong, labelTextScale);
            return;
        }

        _edgeLooks.Clear();
        foreach (Holding h in _visibleHoldings)
        {
            GraphNode a = _view.NodeOf(h.Filer.Id), b = _view.NodeOf(h.Security.Id);
            if (a == null || b == null) continue;

            float width = Mathf.Lerp(minEdgeWidth, maxEdgeWidth, Mathf.Sqrt(Mathf.Clamp01((float)h.Value / _maxPosition)));
            Color color = EdgeColor;
            if (profiled)
            {
                // An edge is part of the search when it joins one hop to the
                // next; the first ring is brightest, later rings fade toward
                // the ordinary edge colour.
                int ha = -1, hb = -1;
                bool inside = _hops.TryGetValue(h.Filer.Id, out ha) && _hops.TryGetValue(h.Security.Id, out hb)
                              && Math.Abs(ha - hb) == 1;
                if (inside)
                {
                    int ring = Math.Min(ha, hb);
                    color = Color.Lerp(EdgeLit, EdgeColor, ring / (float)MaxHops);
                    if (ring == 0) width *= 1.4f;
                }
                else color = EdgeDim;
            }
            _edgeLooks.Add(new GraphView.EdgeLook { from = a, to = b, width = width, color = color });
        }
        _view.DrawEdges(_edgeLooks);
        _view.ShowLabels(labelled, strong, labelTextScale);

        StateChannel.SetState("graph", DescribeState());
    }

    private string DescribeState()
    {
        string what = $"the graph shows {VisibleFilerCount} of {_drawnFilers.Count} filers and {VisibleSecurityCount} of " +
                      $"{_drawnSecurities.Count} securities, joined by {_visibleHoldings.Count} holdings";
        if (_minValue > 0) what += $" of at least {Formatter.Compact(_minValue)} dollars";
        if (_order != GraphOrder.Layout) what += $"; it is sorted by {OrderName(_order)}";
        if (_hops.Count > 0)
            what += $"; {NameOf(_profile.root)} is profiled {_profile.hops} hop{(_profile.hops == 1 ? "" : "s")} out, " +
                    $"reaching {_hops.Count - 1} others";
        return what;
    }

    public static string OrderName(GraphOrder order)
    {
        switch (order)
        {
            case GraphOrder.Value: return "value";
            case GraphOrder.Holders: return "number of holdings";
            case GraphOrder.Name: return "name";
            default: return "its own layout";
        }
    }

    public string NameOf(string id)
    {
        if (id == null || _data == null) return null;
        if (_data.TryGetFiler(id, out Filer f)) return f.Name;
        if (_data.TryGetSecurity(id, out Security s)) return s.DisplayName;
        return null;
    }

    public bool TryNodeWorldPosition(string id, out Vector3 world)
    {
        GraphNode node = _view != null ? _view.NodeOf(id) : null;
        world = node != null ? node.transform.position : Vector3.zero;
        return node != null;
    }

    // ----- Undo -----

    public UndoResult Undo(Edit e)
    {
        if (e == null) return UndoResult.Stale;

        switch (e.kind)
        {
            case EditKind.Move:
            case EditKind.Rotate:
            case EditKind.Scale:
                return RestorePose(e.move.prePos, e.move.preRot, e.move.SafePreScale) ? UndoResult.Applied : UndoResult.Stale;

            case EditKind.Filter:
                if (_data == null) return UndoResult.Unreachable;
                ApplyFilter(e.graphFilterBefore);
                return UndoResult.Applied;

            case EditKind.Profile:
                if (_data == null) return UndoResult.Unreachable;
                _profile = e.graphProfileBefore;
                Redraw();
                OnStateChanged?.Invoke();
                return UndoResult.Applied;

            case EditKind.Sort:
                if (_data == null) return UndoResult.Unreachable;
                _order = e.graphOrderBefore;
                Arrange(animate: true);
                OnStateChanged?.Invoke();
                return UndoResult.Applied;
        }
        return UndoResult.Stale;
    }

    // ----- Grabbing -----

    public void SetGrabbable(bool on)
    {
        _grabbable = on;
        if (_view != null) _view.SetGrabbable(on);
    }

    private Func<GameObject, ITransformer> _twoGrabFor;

    public void SetOneGrab()
    {
        _twoGrabFor = null;
        if (_view != null) _view.SetOneGrab();
    }

    public void SetTwoGrab(Func<GameObject, ITransformer> transformerFor)
    {
        if (transformerFor == null) return;
        _twoGrabFor = transformerFor;
        ApplyTwoGrab();
    }

    private void ApplyTwoGrab()
    {
        if (_twoGrabFor == null || _view == null) return;
        try
        {
            _view.SetTwoGrab(_twoGrabFor(_view.gameObject));
        }
        catch (Exception e)
        {
            Debug.LogError($"[ManageGraph] The graph could not take the two-hand transformer, " +
                           $"so it stays on its previous grab behaviour: {e}");
        }
    }

    public void ResetGrabs()
    {
        _twoGrabFor = null;
        if (_view == null) return;
        _view.ForgetGrabLook();
        _view.transform.localRotation = Quaternion.identity;
        _view.transform.localScale = Vector3.one;
    }

    private void NotifyMoveCommitted(PiecePose before)
    {
        if (_view == null) return;
        MoveCommitted?.Invoke(new PieceMove
        {
            view = ViewKind.Graph,
            sheetId = -1,
            piece = _view.transform,
            root = transform,
            before = before
        });
    }

    private bool RestorePose(Vector3 pos, Quaternion rot, Vector3 scale)
    {
        if (_view == null) return false;
        CancelPieceMotion();
        _view.ForgetGrabLook();
        Transform t = _view.transform;
        t.localPosition = pos;
        t.localRotation = rot;
        t.localScale = scale;
        ApplyTwoGrab();
        return true;
    }

    // ----- IView: what the assistant moves, and the timeline -----

    public ViewKind Kind => ViewKind.Graph;

    event Action IView.PiecesChanged
    {
        add => OnGraphChanged += value;
        remove => OnGraphChanged -= value;
    }

    public bool HasPiece => _view != null && _view.IsBuilt;
    public Transform Root => transform;

    public PiecePose ApplyToPiece(Action<Transform> mutate)
    {
        CompletePieceMotion();
        var before = new PiecePose(_view.transform);
        mutate(_view.transform);
        NotifyMoveCommitted(before);
        AnimatePieceFrom(before);
        return before;
    }

    public PiecePose CommittedPose() => _view.CommittedPose;

    // Nothing the graph draws is rebuilt from the timeline: its undo sets state directly.
    public void SyncToTimeline() { }

    // ----- Motion -----

    private static bool AsAgent => StateChannel.InAgentCall;

    private float ActiveMotionSpeed =>
        AsAgent && AgentMotion.Speed > 0f ? AgentMotion.Speed : systemMotionSpeed;

    private static bool AgentInstant => AsAgent && AgentMotion.Instant;

    private float GrowDuration()
    {
        if (!isActiveAndEnabled || ActiveMotionSpeed <= 0f || AgentInstant) return 0f;
        return MotionDuration(layoutRadius * Mathf.Abs(transform.lossyScale.y));
    }

    private float MotionDuration(float meters) =>
        Mathf.Clamp(meters / Mathf.Max(ActiveMotionSpeed, 1e-3f), minMotionSeconds, maxMotionSeconds);

    private void CompletePieceMotion()
    {
        if (_view != null) _view.CompleteGlide();
    }

    private void CancelPieceMotion()
    {
        if (_view != null) _view.StopGlide();
    }

    private void HaltPieceMotion()
    {
        if (_view != null && _view.StopGlide())
            EditList.Active.AmendNewestPose(ViewKind.Graph, -1, _view.transform, transform);
    }

    private void AnimatePieceFrom(PiecePose before)
    {
        if (_view == null || !isActiveAndEnabled) return;
        if (AgentInstant) { CancelPieceMotion(); return; }
        CompletePieceMotion();

        float meters = GrabbablePiece.GlideMeters(transform, _view.Extent, before, new PiecePose(_view.transform));
        _view.GlideFrom(before, MotionDuration(meters));
    }

    // The user taking over finishes every motion in flight, so what they grab is
    // where it was going rather than somewhere on the way.
    public void Interrupt()
    {
        if (_view != null)
        {
            HaltPieceMotion();
            _view.CompleteGrow();
        }
        CompleteArrange();
    }

    // ----- Placement -----

    private bool ApplyPlacement()
    {
        if (_view == null || !_view.IsBuilt) return false;
        if (!_anchored && !ResolveAnchor()) return false;

        transform.SetPositionAndRotation(_anchorCenter, _anchorYaw);
        return true;
    }

    // In front of the viewer by the graph's own radius past the near limit, so
    // its near side is never inside their head, and at a fraction of their eye
    // height, so a shorter or seated user gets it lower by the same proportion.
    private bool ResolveAnchor()
    {
        if (!CameraRig.TryGetBasis(out Vector3 camPos, out Quaternion yaw)) return false;

        // Beside the sheet when both are in the room, so neither stands inside the other.
        float right = Views.Both ? besideSheetOffset : 0f;
        _anchorCenter = camPos + yaw * new Vector3(right, 0f, Mathf.Max(minimumZOffsetFromCamera, 0f) + layoutRadius);
        _anchorCenter.y = camPos.y * heightFraction;
        _anchorYaw = yaw;
        _anchored = true;
        return true;
    }

    public void RecenterToUser()
    {
        _anchored = false;
        _placementPending = true;
    }
}
