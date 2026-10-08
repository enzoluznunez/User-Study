using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The holdings network as one object in the room: a node per filer and per
// security, an edge per holding, and the names above them. It is what the hands
// grab (GrabbablePiece), so Move, Rotate and Scale act on it whole, and it
// draws whatever state ManageGraph hands it without deciding any of that state.
public class GraphView : GrabbablePiece
{
    private readonly List<GraphNode> _nodes = new List<GraphNode>();
    private readonly Dictionary<string, GraphNode> _byId = new Dictionary<string, GraphNode>();

    private Transform _content;
    private MeshFilter _edgeFilter;
    private MeshRenderer _edgeRenderer;
    private Mesh _edgeMesh;
    private GraphLabels _labels;


    private Coroutine _grow;
    private float _pendingGrow = -1f;


    public IReadOnlyList<GraphNode> Nodes => _nodes;
    public bool IsBuilt => _nodes.Count > 0;

    public GraphNode NodeOf(string id) =>
        id != null && _byId.TryGetValue(id, out GraphNode node) ? node : null;

    // ----- Building -----

    public void Build(Material material, IReadOnlyList<(Filer filer, Security security, Vector3 local, float radius)> nodes)
    {
        EnsureContent(material);

        // Nodes are kept by id across builds, so a rebuild that draws the same
        // data again does not tear down and recreate every object.
        var wanted = new HashSet<string>();
        _nodes.Clear();
        foreach (var n in nodes)
        {
            string id = n.filer != null ? n.filer.Id : n.security.Id;
            wanted.Add(id);
            if (!_byId.TryGetValue(id, out GraphNode node) || node == null)
            {
                GameObject go = new GameObject(id);
                go.transform.SetParent(_content, false);
                node = go.AddComponent<GraphNode>();
                node.Init(this, material, n.filer, n.security);
                _byId[id] = node;
            }
            node.Place(n.local, n.radius);
            node.SetVisible(true);
            _nodes.Add(node);
        }

        var stale = new List<string>();
        foreach (var kv in _byId)
            if (!wanted.Contains(kv.Key)) stale.Add(kv.Key);
        foreach (string id in stale)
        {
            if (_byId[id] != null) Destroy(_byId[id].gameObject);
            _byId.Remove(id);
        }

        FitBounds();
    }

    private void EnsureContent(Material material)
    {
        if (_content == null)
        {
            _content = new GameObject("Content").transform;
            _content.SetParent(transform, false);
        }

        if (_edgeFilter == null)
        {
            GameObject edges = new GameObject("Edges");
            edges.transform.SetParent(_content, false);
            _edgeFilter = edges.AddComponent<MeshFilter>();
            _edgeRenderer = edges.AddComponent<MeshRenderer>();
            _edgeMesh = new Mesh { name = "GraphEdges", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _edgeMesh.MarkDynamic();
            _edgeFilter.sharedMesh = _edgeMesh;
        }
        if (material != null) _edgeRenderer.sharedMaterial = material;

        if (_labels == null) _labels = new GraphLabels(_content);
    }

    // ----- Drawing state -----

    public struct EdgeLook
    {
        public GraphNode from;
        public GraphNode to;
        public float width;
        public Color color;
    }

    private readonly List<Vector3> _verts = new List<Vector3>();
    private readonly List<Color> _colors = new List<Color>();
    private readonly List<int> _tris = new List<int>();

    // Every edge is a thin square tube from one node's centre to the other's, all
    // in one mesh: a few hundred edges cost one draw rather than a few hundred.
    public void DrawEdges(IReadOnlyList<EdgeLook> edges)
    {
        if (_edgeMesh == null) return;

        _verts.Clear();
        _colors.Clear();
        _tris.Clear();

        for (int i = 0; i < edges.Count; i++)
        {
            EdgeLook e = edges[i];
            if (e.from == null || e.to == null) continue;

            Vector3 a = e.from.transform.localPosition;
            Vector3 b = e.to.transform.localPosition;
            Vector3 d = b - a;
            if (d.sqrMagnitude < 1e-10f) continue;

            Vector3 dir = d.normalized;
            Vector3 u = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(dir, u);
            float h = e.width * 0.5f;

            Vector3 c0 = (u + v) * h, c1 = (u - v) * h, c2 = (-u - v) * h, c3 = (-u + v) * h;
            int at = _verts.Count;
            _verts.Add(a + c0); _verts.Add(a + c1); _verts.Add(a + c2); _verts.Add(a + c3);
            _verts.Add(b + c0); _verts.Add(b + c1); _verts.Add(b + c2); _verts.Add(b + c3);
            for (int k = 0; k < 8; k++) _colors.Add(e.color);

            for (int k = 0; k < 4; k++)
            {
                int k2 = (k + 1) % 4;
                int a0 = at + k, a1 = at + k2, b0 = at + 4 + k, b1 = at + 4 + k2;
                // Both windings: the material culls back faces, and a tube this
                // thin is not worth getting the winding right for each side.
                _tris.Add(a0); _tris.Add(b0); _tris.Add(a1);
                _tris.Add(a1); _tris.Add(b0); _tris.Add(b1);
                _tris.Add(a0); _tris.Add(a1); _tris.Add(b0);
                _tris.Add(a1); _tris.Add(b1); _tris.Add(b0);
            }
        }

        _edgeMesh.Clear();
        _edgeMesh.SetVertices(_verts);
        _edgeMesh.SetColors(_colors);
        _edgeMesh.SetTriangles(_tris, 0, true);
    }

    public void ShowLabels(IReadOnlyList<GraphNode> nodes, IReadOnlyCollection<GraphNode> emphasised, float textScale)
    {
        if (_labels != null) _labels.Show(nodes, emphasised, textScale);
    }

    private void LateUpdate()
    {
        if (_labels != null) _labels.Follow();
    }

    // ----- Growing in -----

    public void PlayGrow(float duration)
    {
        if (duration <= 0f || _content == null) return;

        if (_grow != null) { StopCoroutine(_grow); _grow = null; }
        _content.localScale = Vector3.one * 0.01f;
        if (!isActiveAndEnabled) { _pendingGrow = duration; return; }
        _grow = StartCoroutine(GrowRoutine(duration));
    }

    public void CompleteGrow()
    {
        _pendingGrow = -1f;
        if (_grow != null) { StopCoroutine(_grow); _grow = null; }
        if (_content != null) _content.localScale = Vector3.one;
    }

    private IEnumerator GrowRoutine(float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            yield return null;
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0.01f, 1f, Mathf.Clamp01(t / duration));
            _content.localScale = Vector3.one * k;
        }
        _content.localScale = Vector3.one;
        _grow = null;
    }

    private void OnEnable()
    {
        if (_pendingGrow <= 0f) return;
        float duration = _pendingGrow;
        _pendingGrow = -1f;
        _grow = StartCoroutine(GrowRoutine(duration));
    }

    // ----- Being grabbed -----

    public void SetPickable(bool on)
    {
        Bounds.enabled = on;
        for (int i = 0; i < _nodes.Count; i++) _nodes[i].SetPickable(on);
    }

    // After nodes move, so the grab volume still holds them all.
    public void RefitBounds() => FitBounds();

    private void FitBounds()
    {
        BoxCollider bounds = Bounds;
        if (_nodes.Count == 0) { bounds.size = Vector3.one * 1e-3f; return; }

        Bounds b = new Bounds(_nodes[0].transform.localPosition, Vector3.zero);
        for (int i = 0; i < _nodes.Count; i++)
        {
            GraphNode n = _nodes[i];
            b.Encapsulate(new Bounds(n.transform.localPosition, Vector3.one * n.Radius * 2f));
        }
        bounds.center = b.center;
        bounds.size = b.size;
    }

    public float Extent => Bounds.size.magnitude * 0.5f;

    private void OnDestroy()
    {
        if (_edgeMesh != null) Destroy(_edgeMesh);
    }
}
