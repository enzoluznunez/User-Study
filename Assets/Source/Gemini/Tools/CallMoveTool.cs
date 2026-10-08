using System.Collections.Generic;
using Google.GenAI.Types;
using UnityEngine;

public sealed class CallMoveTool : AgenticTool<CallMoveTool.Args> {

    private const float DefaultDistance = 0.15f;
    private const float MinDistance = 0.001f;
    private const float MaxDistance = 100f;

    public class Args {
        [Doc("Which way to slide it, from the user's point of view; 'forward' is away from them.")]
        [Values("left", "right", "forward", "back", "up", "down",
                "forward-left", "forward-right", "back-left", "back-right")]
        public string direction;
        [Doc("How far to slide, in meters, from a millimetre upward."), Limits(0.001, 100), DefaultsTo(0.15), Optional]
        public float? distance;
        [Doc("Which view to move, when both the graph and the sheet are in the room."), Values("graph", "sheet"), Optional]
        public string view;
    }

    protected override bool EditsAreOutcome => true;

    public override FunctionDeclaration Declaration => new FunctionDeclaration {
        Name = "CallMoveTool",
        Description = "Move the graph or the sheet, as if the user grabbed and slid it: a distance in a direction, " +
                      "including diagonals. It passes freely through anything in the room.",
        Parameters = ParametersFor(typeof(Args))
    };

    protected override void Run(Args args, Dictionary<string, object> result) {
        if (!EnsureToolSelected(ToolType.Move, result)) return;

        string dir = args.direction?.Trim().ToLowerInvariant();
        if (!TryWorldDirection(dir, out Vector3 dirVec)) {
            result["error"] = $"Unknown direction '{args.direction}'. Use left, right, forward, back, up, down, " +
                              "or a diagonal such as forward-left.";
            return;
        }

        if (!TryResolvePiece(args.view, "move", result, out IView piece)) return;

        float asked = args.distance ?? DefaultDistance;
        float distance = Mathf.Clamp(asked, MinDistance, MaxDistance);
        Vector3 delta = dirVec * distance;

        var pre = piece.ApplyToPiece(t => t.localPosition += piece.Root.InverseTransformVector(delta));
        float actual = piece.Root.TransformVector(piece.CommittedPose().pos - pre.pos).magnitude;

        result["view"] = Views.Name(piece.Kind);
        result["moved"] = dir;
        result["actualMeters"] = Round(actual);
        result["requestedMeters"] = Round(asked);
        if (distance != asked)
            result["note"] = $"The distance was limited to the {MinDistance} to {MaxDistance} meter range; {distance} m was used.";
    }
}
