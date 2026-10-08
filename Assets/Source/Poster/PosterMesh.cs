using System.Collections.Generic;
using UnityEngine;

// Geometry for the poster figures, accumulated into one mesh per view.
//
// Shading is baked per vertex (VertexLight) rather than lit at draw time, which
// keeps the figures on the same material as the headset's views and makes a
// capture reproducible -- no scene light, no exposure, no pipeline settings to
// match.
public class PosterMesh
{
    private readonly List<Vector3> _verts = new List<Vector3>();
    private readonly List<Color> _colors = new List<Color>();
    private readonly List<int> _tris = new List<int>();

    public static Color Shade(Color color, Vector3 normal) => Shade(color, normal, 1f);

    // The exposure argument is the second depth cue: geometry alone cannot tell
    // a bowl from a dome once the silhouette is ambiguous, but a hollow that
    // darkens as it deepens is read as a hollow immediately. The caller passes
    // how open to the sky a point is, and it multiplies the lighting.
    public static Color Shade(Color color, Vector3 normal, float exposure)
    {
        Color linear = VertexLight.ToLinear(color);
        float light = VertexLight.Of(normal, exposure);
        return new Color(linear.r * light, linear.g * light, linear.b * light, color.a);
    }

    public Mesh Build(string name)
    {
        var mesh = new Mesh { name = name };
        if (_verts.Count > ushort.MaxValue) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(_verts);
        mesh.SetColors(_colors);
        mesh.SetTriangles(_tris, 0, true);
        mesh.RecalculateBounds();
        return mesh;
    }

    // Corners wound a, b, c, d around the face.
    public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                        Color ca, Color cb, Color cc, Color cd)
    {
        int at = _verts.Count;
        _verts.Add(a); _verts.Add(b); _verts.Add(c); _verts.Add(d);
        _colors.Add(ca); _colors.Add(cb); _colors.Add(cc); _colors.Add(cd);
        _tris.Add(at); _tris.Add(at + 1); _tris.Add(at + 2);
        _tris.Add(at); _tris.Add(at + 2); _tris.Add(at + 3);
    }

    public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color) =>
        AddQuad(a, b, c, d, color, color, color, color);

    // A UV sphere. The node marker: round from every angle, and shaded so a
    // ring of ten of them still reads as ten solids rather than ten flat discs.
    public void AddSphere(Vector3 center, float radius, Color color, float exposure = 1f,
                          int rings = 18, int segments = 28)
    {
        int at = _verts.Count;

        for (int ring = 0; ring <= rings; ring++)
        {
            float v = (float)ring / rings;
            float phi = v * Mathf.PI;
            float y = Mathf.Cos(phi);
            float r = Mathf.Sin(phi);

            for (int seg = 0; seg <= segments; seg++)
            {
                float u = (float)seg / segments;
                float theta = u * Mathf.PI * 2f;
                var normal = new Vector3(r * Mathf.Cos(theta), y, r * Mathf.Sin(theta));
                _verts.Add(center + normal * radius);
                _colors.Add(Shade(color, normal, exposure));
            }
        }

        int stride = segments + 1;
        for (int ring = 0; ring < rings; ring++)
        {
            for (int seg = 0; seg < segments; seg++)
            {
                int i0 = at + ring * stride + seg;
                int i1 = i0 + stride;
                _tris.Add(i0); _tris.Add(i1); _tris.Add(i0 + 1);
                _tris.Add(i0 + 1); _tris.Add(i1); _tris.Add(i1 + 1);
            }
        }
    }

    // A flat ribbon from a to b in the XY plane: the edges of the network.
    public void AddRibbon(Vector3 a, Vector3 b, float width, Color colorA, Color colorB)
    {
        Vector3 along = b - a;
        if (along.sqrMagnitude < 1e-8f) return;

        Vector3 side = Vector3.Cross(along.normalized, Vector3.forward).normalized * (width * 0.5f);
        AddQuad(a - side, b - side, b + side, a + side, colorA, colorB, colorB, colorA);
    }

    // Shortens a segment at both ends so an edge meets the rim of a node rather
    // than running under it, which would darken the sphere it passes beneath.
    public static void Trim(ref Vector3 a, ref Vector3 b, float startGap, float endGap)
    {
        Vector3 along = b - a;
        float length = along.magnitude;
        if (length <= startGap + endGap) return;

        Vector3 unit = along / length;
        a += unit * startGap;
        b -= unit * endGap;
    }
}
