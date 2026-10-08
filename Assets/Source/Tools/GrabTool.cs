using UnityEngine;
using Oculus.Interaction;

// What Move, Rotate and Scale share: each arms every view in the room for
// grabbing while it is the selected tool, and records the grab the user lets
// go of as one edit on the timeline, on whichever view was grabbed. They differ
// only in how the hands take hold.
public abstract class GrabTool : Tool
{
    private bool _armPending;

    protected abstract EditKind EditKind { get; }

    protected abstract string Verb(string what, float metres);

    // Move slides with one hand; Rotate and Scale take both, through the
    // transformer TwoHand makes for the piece.
    protected virtual bool TwoHanded => true;

    protected virtual ITransformer TwoHand(GameObject piece) => null;

    protected override void OnToolStart()
    {
        foreach (IView view in Scene.Views)
        {
            view.PiecesChanged += RequestArm;
            view.MoveCommitted += OnMoveCommitted;
        }
    }

    protected override void OnToolDestroy()
    {
        foreach (IView view in Scene.Views)
        {
            view.PiecesChanged -= RequestArm;
            view.MoveCommitted -= OnMoveCommitted;
        }
    }

    protected override void OnResetTool()
    {
        foreach (IView view in Scene.Views) view.ResetGrabs();
    }

    protected override void OnActiveChanged(bool active)
    {
        if (active) { _armPending = true; return; }
        _armPending = false;
        foreach (IView view in Scene.Views) view.SetGrabbable(false);
    }

    private void OnMoveCommitted(PieceMove move)
    {
        if (!Active || move.piece == null || toolManager == null) return;

        float distance = move.root.TransformVector(move.piece.localPosition - move.before.pos).magnitude;
        Report(Verb(Views.Name(move.view), distance));

        EditList.Active.PushMove(new MoveRecord
        {
            sheetId = move.sheetId,
            prePos = move.before.pos,
            preRot = move.before.rot,
            preScale = move.before.scale,
            distance = distance
        }, EditKind, move.view);
    }

    private void RequestArm()
    {
        if (Active) _armPending = true;
    }

    // A frame late, so the piece a rebuild has just made is the one armed.
    private void LateUpdate()
    {
        if (!_armPending) return;
        _armPending = false;
        if (!Active) return;

        foreach (IView view in Scene.Views)
        {
            if (TwoHanded)
            {
                view.SetGrabbable(true);
                view.SetTwoGrab(TwoHand);
            }
            else
            {
                view.SetOneGrab();
                view.SetGrabbable(true);
            }
        }
    }
}
