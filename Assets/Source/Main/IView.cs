using System;
using UnityEngine;
using Oculus.Interaction;

// Where a piece stands: its local position, rotation and scale.
public struct PiecePose
{
    public readonly Vector3 pos;
    public readonly Quaternion rot;
    public readonly Vector3 scale;

    public PiecePose(Transform t) { pos = t.localPosition; rot = t.localRotation; scale = t.localScale; }
    public PiecePose(Vector3 p, Quaternion r, Vector3 s) { pos = p; rot = r; scale = s; }
}

// A grab let go: which piece, of which view, and where it stood before.
public struct PieceMove
{
    public ViewKind view;
    public int sheetId;
    public Transform piece;
    public Transform root;
    public PiecePose before;
}

// What every view of the holdings does the same way, so the tools, Undo and
// the assistant can work through views without asking which one they hold.
// ManageSheets and ManageGraph each implement it; Scene.Views lists them.
public interface IView
{
    ViewKind Kind { get; }

    // ----- The hands -----

    // Raised when the view rebuilds its pieces, which the grab tools re-arm on.
    event Action PiecesChanged;
    event Action<PieceMove> MoveCommitted;

    void SetGrabbable(bool on);
    void SetOneGrab();
    void SetTwoGrab(Func<GameObject, ITransformer> transformerFor);
    void ResetGrabs();

    // ----- The assistant -----

    bool HasPiece { get; }
    Transform Root { get; }

    // Changes the piece as a grab would: finishes any glide, applies 'mutate',
    // records the move, and animates from where it was. Returns where it was.
    PiecePose ApplyToPiece(Action<Transform> mutate);

    // Where the piece will stand once any glide in flight has finished.
    PiecePose CommittedPose();

    // ----- The timeline -----

    UndoResult Undo(Edit e);

    // After the timeline changes: bring anything drawn from it up to date.
    void SyncToTimeline();

    // The user took over: finish every motion in flight.
    void Interrupt();
}

// How fast the assistant's own actions play out, as the panel's speed row
// sets it: a speed in metres per second, or instant. Both views read it.
public static class AgentMotion
{
    public static float Speed { get; private set; } = -1f;
    public static bool Instant { get; private set; }

    public static void Set(float speed, bool instant)
    {
        Speed = speed;
        Instant = instant;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Set(-1f, false);
}
