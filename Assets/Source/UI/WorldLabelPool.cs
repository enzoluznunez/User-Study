using System.Collections.Generic;
using TMPro;
using UnityEngine;

// World-space text labels kept for reuse under a "Labels" child of their
// owner: the sheet's row and column titles and the graph's node names. A
// label asked for past the end is made; the ones not asked for this time are
// hidden rather than destroyed.
public class WorldLabelPool
{
    private readonly Transform _owner;
    private readonly Vector2 _box;
    private readonly TextAlignmentOptions _alignment;
    private readonly Vector2 _pivot;
    private readonly List<TextMeshPro> _labels = new List<TextMeshPro>();
    private Transform _root;

    public WorldLabelPool(Transform owner, Vector2 box, TextAlignmentOptions alignment, Vector2 pivot)
    {
        _owner = owner;
        _box = box;
        _alignment = alignment;
        _pivot = pivot;
    }

    public TextMeshPro this[int index] => _labels[index];

    public TextMeshPro Get(int index)
    {
        if (_root == null)
        {
            if (_owner == null) return null;
            _root = new GameObject("Labels").transform;
            _root.SetParent(_owner, false);
        }

        while (_labels.Count <= index)
        {
            GameObject go = new GameObject($"Label_{_labels.Count}");
            go.transform.SetParent(_root, false);

            TextMeshPro label = go.AddComponent<TextMeshPro>();
            Style.ApplyBody(label);
            label.alignment = _alignment;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.rectTransform.sizeDelta = _box;
            label.rectTransform.pivot = _pivot;
            _labels.Add(label);
        }
        return _labels[index];
    }

    // Hides every label from 'used' on: the ones this pass did not ask for.
    public void HideFrom(int used)
    {
        for (int i = used; i < _labels.Count; i++)
            if (_labels[i] != null) _labels[i].gameObject.SetActive(false);
    }
}
