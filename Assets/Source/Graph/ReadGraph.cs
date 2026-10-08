using UnityEngine;

// What the fingertip is over on the graph: hovering a node, pressing into it,
// and letting go (see ReadHub).
public class ReadGraph : ReadHub<ReadGraph.Reading, GraphTouch>
{
    public struct Reading
    {
        public bool valid;
        public GraphNode node;
        public Vector3 point;
        public Vector3 tip;
    }

    protected override bool IsValid(Reading reading) => reading.valid;

    // A poke commits on the node it started on.
    protected override UnityEngine.Object PressTarget(Reading reading) => reading.node;

    public static Reading Describe(GraphNode node, Vector3 point, Vector3 tip) => new Reading
    {
        valid = node != null,
        node = node,
        point = point,
        tip = tip
    };
}
