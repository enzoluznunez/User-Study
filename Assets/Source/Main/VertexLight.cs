using UnityEngine;

// Lighting baked into vertex colours. The material the app draws with is unlit
// and reads vertex colour, so shape reads only if light and shade are painted
// on: one fixed key direction, wrapped into an ambient floor so a face turned
// away is dimmer but never black. The poster figures and the graph's nodes are
// lit by this one rule, so they shade alike.
public static class VertexLight
{
    // Oblique rather than overhead: a surface whose normals all point roughly
    // up takes almost no shading from a light above it.
    public static readonly Vector3 KeyDirection = new Vector3(-0.58f, 0.40f, -0.71f).normalized;

    private const float Ambient = 0.30f;
    private const float Key = 0.85f;

    // How much light a surface facing 'normal' takes, times how open to the
    // sky it is.
    public static float Of(Vector3 normal, float exposure = 1f)
    {
        float lambert = Mathf.Clamp01(Vector3.Dot(normal.normalized, KeyDirection));
        return (Ambient + Key * lambert) * Mathf.Clamp01(exposure);
    }

    // Palette colours are sRGB, but a vertex colour reaches the shader
    // unconverted and the project renders linear; converting here keeps what
    // is drawn the colour the palette names.
    public static Color ToLinear(Color color) =>
        QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
}
