using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

// The names floating above nodes. A label turns to face the viewer every frame,
// since a graph is walked around rather than read from one side, and it sits
// just above its node so it never hides the node it names.
public class GraphLabels
{
    private static readonly Vector2 LabelBox = new Vector2(30f, 4f);

    // Long names are cut to this many characters, ellipsis included: a filer's
    // full legal name runs past forty and would run into its neighbours.
    private const int MaxChars = 30;

    private readonly Transform _owner;
    private readonly WorldLabelPool _pool;
    private readonly List<GraphNode> _nodes = new List<GraphNode>();

    public GraphLabels(Transform owner)
    {
        _owner = owner;
        _pool = new WorldLabelPool(owner, LabelBox, TextAlignmentOptions.Bottom, new Vector2(0.5f, 0f));
    }

    public static string Shorten(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length <= MaxChars) return name;
        return name.Substring(0, MaxChars - 1).TrimEnd(' ', ',', '.', '·') + "…";
    }

    public void Show(IReadOnlyList<GraphNode> nodes, IReadOnlyCollection<GraphNode> emphasised, float textScale)
    {
        _nodes.Clear();
        int used = 0;

        for (int i = 0; i < nodes.Count; i++)
        {
            GraphNode node = nodes[i];
            if (node == null || !node.IsVisible) continue;

            TextMeshPro label = _pool.Get(used++);
            if (label == null) break;

            bool strong = emphasised != null && emphasised.Contains(node);
            label.text = strong ? node.Label : Shorten(node.Label);
            label.color = strong ? Style.White : new Color(1f, 1f, 1f, 0.8f);
            label.fontStyle = strong ? FontStyles.Bold : FontStyles.Normal;
            label.transform.localScale = Vector3.one * (Style.WorldTextScale * textScale * (strong ? 1.15f : 1f));
            label.gameObject.SetActive(true);
            _nodes.Add(node);
        }

        _pool.HideFrom(used);

        Follow();
    }

    public void Clear() => Show(System.Array.Empty<GraphNode>(), null, 1f);

    // Every frame: each label rides above its node and turns to the viewer.
    public void Follow()
    {
        if (_nodes.Count == 0) return;
        Transform cam = CameraRig.MainTransform;
        float gap = 0.012f * _owner.lossyScale.y;

        for (int i = 0; i < _nodes.Count; i++)
        {
            GraphNode node = _nodes[i];
            TextMeshPro label = _pool[i];
            if (node == null || label == null) continue;

            Transform t = label.transform;
            Vector3 at = node.transform.position + Vector3.up * (node.transform.lossyScale.y * 0.5f + gap);
            Vector3 away = cam != null ? at - cam.position : Vector3.zero;
            if (away.sqrMagnitude > 1e-6f) t.SetPositionAndRotation(at, Quaternion.LookRotation(away, Vector3.up));
            else t.position = at;
        }
    }
}
