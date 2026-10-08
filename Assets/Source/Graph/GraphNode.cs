using UnityEngine;

// One node of the holdings network: a filer, drawn as a cube, or a security,
// drawn as a sphere. The shape says which kind at a glance, before the colour
// does, so the two still read apart for someone who cannot tell the hues.
//
// The material is unlit vertex colour, so shading is baked in (VertexLight),
// the same light the poster figures take: a lit side and a dark side are what
// make a node read as solid rather than as a flat disc.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(SphereCollider))]
public class GraphNode : MonoBehaviour
{
    public string Id { get; private set; }
    public bool IsFiler { get; private set; }
    public Filer Filer { get; private set; }
    public Security Security { get; private set; }
    public GraphView View { get; private set; }

    public string Label => IsFiler ? Filer.Name : Security.DisplayName;

    private MeshFilter _filter;
    private MeshRenderer _renderer;
    private SphereCollider _collider;
    private Mesh _mesh;
    private Color[] _colors;
    private float[] _shade;

    private Color _painted;
    private bool _hasPaint;
    private float _radius = 0.01f;
    private float _swell;
    private bool _visible = true;

    public float Radius => _radius;
    public bool IsVisible => _visible;
    public SphereCollider Collider => _collider;

    public void Init(GraphView view, Material material, Filer filer, Security security)
    {
        View = view;
        Filer = filer;
        Security = security;
        IsFiler = filer != null;
        Id = IsFiler ? filer.Id : security.Id;
        name = $"Node_{Id}";

        if (_filter == null) _filter = GetComponent<MeshFilter>();
        if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
        if (_collider == null) _collider = GetComponent<SphereCollider>();
        if (material != null) _renderer.sharedMaterial = material;

        _collider.center = Vector3.zero;
        _collider.radius = 0.5f;
        _collider.isTrigger = true;

        if (_mesh == null)
        {
            _mesh = IsFiler ? Shapes.Cube() : Shapes.Sphere();
            _filter.sharedMesh = _mesh;

            Vector3[] normals = _mesh.normals;
            _shade = new float[normals.Length];
            for (int i = 0; i < normals.Length; i++)
                _shade[i] = VertexLight.Of(normals[i]);
            _colors = new Color[normals.Length];
        }
    }

    public void Place(Vector3 local, float radius)
    {
        transform.localPosition = local;
        transform.localRotation = Quaternion.identity;
        _radius = Mathf.Max(radius, 1e-4f);
        ApplyScale();
    }

    public void SetColor(Color color)
    {
        if (_hasPaint && _painted == color) return;
        _painted = color;
        _hasPaint = true;

        Color linear = VertexLight.ToLinear(color);
        for (int i = 0; i < _colors.Length; i++)
        {
            float s = _shade[i];
            _colors[i] = new Color(linear.r * s, linear.g * s, linear.b * s, 1f);
        }
        _mesh.SetColors(_colors);
    }

    // How much bigger than its size the node is drawn: a hovered or focused node
    // swells so the eye finds it, and the collider swells with it.
    public void SetSwell(float swell)
    {
        if (Mathf.Approximately(_swell, swell)) return;
        _swell = swell;
        ApplyScale();
    }

    private void ApplyScale()
    {
        float d = 2f * _radius * (1f + Mathf.Max(_swell, 0f));
        transform.localScale = new Vector3(d, d, d);
    }

    public void SetVisible(bool visible)
    {
        if (_visible == visible) return;
        _visible = visible;
        if (_renderer != null) _renderer.enabled = visible;
        if (_collider != null) _collider.enabled = visible;
    }

    public void SetPickable(bool on)
    {
        if (_collider != null) _collider.enabled = on && _visible;
    }

    private void OnDestroy()
    {
        if (_mesh != null) Destroy(_mesh);
    }
}

// Unit meshes the nodes are cut from, each a fresh copy so a node can paint its
// own vertex colours.
public static class Shapes
{
    public static Mesh Sphere(int rings = 10, int segments = 16)
    {
        var verts = new System.Collections.Generic.List<Vector3>();
        var normals = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();

        for (int r = 0; r <= rings; r++)
        {
            float phi = Mathf.PI * r / rings;
            float y = Mathf.Cos(phi), ring = Mathf.Sin(phi);
            for (int s = 0; s <= segments; s++)
            {
                float theta = 2f * Mathf.PI * s / segments;
                Vector3 n = new Vector3(ring * Mathf.Cos(theta), y, ring * Mathf.Sin(theta));
                normals.Add(n);
                verts.Add(n * 0.5f);
            }
        }

        int stride = segments + 1;
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = r * stride + s, b = a + stride;
                tris.Add(a); tris.Add(a + 1); tris.Add(b);
                tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
            }

        var mesh = new Mesh { name = "NodeSphere" };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
        return mesh;
    }

    // Slightly smaller than the sphere it stands beside, so a cube and a sphere
    // of one value look the same size rather than the cube looking larger.
    public static Mesh Cube(float side = 0.8f)
    {
        float h = side * 0.5f;
        Vector3[] faces = { Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
        var verts = new System.Collections.Generic.List<Vector3>();
        var normals = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();

        foreach (Vector3 n in faces)
        {
            Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
            Vector3 v = Vector3.Cross(n, u);
            int at = verts.Count;
            verts.Add((n - u - v) * h); verts.Add((n + u - v) * h);
            verts.Add((n + u + v) * h); verts.Add((n - u + v) * h);
            for (int i = 0; i < 4; i++) normals.Add(n);
            tris.Add(at); tris.Add(at + 1); tris.Add(at + 2);
            tris.Add(at); tris.Add(at + 2); tris.Add(at + 3);
        }

        var mesh = new Mesh { name = "NodeCube" };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one);
        return mesh;
    }
}
