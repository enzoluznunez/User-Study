using UnityEngine;

// Feeds ReadGraph from the hands' fingertips: the nearest visible node is
// hovered, pressed and let go of.
[RequireComponent(typeof(ReadGraph))]
public class GraphTouch : PokeTouch<GraphNode>
{
    private ReadGraph _hub;

    private void Awake() => _hub = GetComponent<ReadGraph>();

    protected override bool Listening => _hub != null && _hub.Listening;

    protected override bool Accept(GraphNode node) => node.IsVisible;

    protected override void Hover(PokeHit<GraphNode> hit, Vector3 tip, Vector3 wrist) =>
        _hub.Hover(ReadGraph.Describe(hit.target, hit.point, tip));

    protected override void Select(PokeHit<GraphNode> hit, Vector3 tip, Vector3 wrist) =>
        _hub.Select(ReadGraph.Describe(hit.target, hit.point, tip));

    protected override void Release(PokeHit<GraphNode> hit, Vector3 tip, Vector3 wrist) =>
        _hub.Release(ReadGraph.Describe(hit.target, hit.point, tip));

    protected override void ReleaseAt(GraphNode node) =>
        _hub.Release(ReadGraph.Describe(node, node.transform.position, node.transform.position));

    protected override void Cleared() => _hub.Cleared();
}
