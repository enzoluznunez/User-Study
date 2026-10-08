using UnityEngine;
using Oculus.Interaction;

public class RotateTool : GrabTool
{
    protected override ITransformer TwoHand(GameObject piece)
    {
        TwoGrabRotateTransformer yaw = piece.GetComponent<TwoGrabRotateTransformer>();
        if (yaw != null) return yaw;

        yaw = piece.AddComponent<TwoGrabRotateTransformer>();
        yaw.InjectOptionalConstraints(new TwoGrabRotateTransformer.TwoGrabRotateConstraints
        {
            MinAngle = new FloatConstraint(),
            MaxAngle = new FloatConstraint()
        });
        return yaw;
    }

    protected override ToolType Kind => ToolType.Rotate;
    protected override EditKind EditKind => EditKind.Rotate;
    protected override string Verb(string what, float metres) => $"rotated the {what}";
}
