using System.Collections;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;

// What the hands take hold of: a bar sheet or the network graph. Each sits on a
// prefab with a box collider, a kinematic rigidbody and the Interaction SDK's
// grab components, and this is the one place those are driven from, so the
// sheet and the graph grab, slide, turn and resize the same way.
[RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
public abstract class GrabbablePiece : MonoBehaviour
{
    private BoxCollider _bounds;
    private Rigidbody _body;
    private Grabbable _grabbable;
    private HandGrabInteractable _handGrab;
    private OneGrabTranslateTransformer _slide;

    private bool _wasGrabbed;
    private Vector3 _grabPos;
    private Quaternion _grabRot;
    private Vector3 _grabScale;

    private bool _grabbedLook;
    private Vector3 _restScale = Vector3.one;

    // The volume the hands can grab: fitted around whatever the piece draws.
    protected BoxCollider Bounds
    {
        get { EnsureComponents(); return _bounds; }
    }

    private void EnsureComponents()
    {
        if (_bounds == null) _bounds = GetComponent<BoxCollider>();
        if (_body == null)
        {
            _body = GetComponent<Rigidbody>();
            _body.isKinematic = true;
            _body.useGravity = false;
        }
        if (_grabbable == null) _grabbable = GetComponentInChildren<Grabbable>(true);
        if (_grabbable != null && _grabbable.Transform == null)
            _grabbable.InjectOptionalTargetTransform(_grabbable.transform);
        if (_handGrab == null) _handGrab = GetComponentInChildren<HandGrabInteractable>(true);
        if (_slide == null) _slide = GetComponent<OneGrabTranslateTransformer>();
    }

    public bool IsGrabbed
    {
        get
        {
            EnsureComponents();
            return _grabbable != null && _grabbable.SelectingPointsCount > 0;
        }
    }

    // Swells a little while held, so the hand can see it has hold.
    public void SetGrabLook(bool on)
    {
        if (_grabbedLook == on) return;
        if (on)
        {
            _restScale = transform.localScale;
            transform.localScale = _restScale * Style.EngageScale;
        }
        else transform.localScale = _restScale;
        _grabbedLook = on;
    }

    public void ForgetGrabLook()
    {
        _grabbedLook = false;
        _restScale = transform.localScale;
    }

    // True on the frame a grab ends, with the pose it started from.
    public bool PollGrabRelease(out Vector3 prePos, out Quaternion preRot, out Vector3 preScale)
    {
        prePos = Vector3.zero;
        preRot = Quaternion.identity;
        preScale = Vector3.one;

        bool grabbed = IsGrabbed;
        if (grabbed == _wasGrabbed) return false;
        _wasGrabbed = grabbed;

        if (grabbed)
        {
            _grabPos = transform.localPosition;
            _grabRot = transform.localRotation;
            _grabScale = transform.localScale;
            return false;
        }

        prePos = _grabPos;
        preRot = _grabRot;
        preScale = _grabScale;
        return true;
    }

    // Ends a grab still in progress as if it had been let go, for a piece
    // about to be taken away under the hand.
    public bool ForceGrabRelease(out Vector3 prePos, out Quaternion preRot, out Vector3 preScale)
    {
        prePos = Vector3.zero;
        preRot = Quaternion.identity;
        preScale = Vector3.one;

        if (!_wasGrabbed) return false;
        _wasGrabbed = false;

        prePos = _grabPos;
        preRot = _grabRot;
        preScale = _grabScale;
        return true;
    }

    public void SetGrabbable(bool on)
    {
        EnsureComponents();
        if (_grabbable != null) _grabbable.enabled = on;
        if (_handGrab != null) _handGrab.enabled = on;
    }

    public void SetOneGrab()
    {
        EnsureComponents();
        if (_grabbable == null) return;

        _grabbable.MaxGrabPoints = 1;
        if (_slide != null) _grabbable.InjectOptionalOneGrabTransformer(_slide);
    }

    public void SetTwoGrab(ITransformer transformer)
    {
        EnsureComponents();
        if (_grabbable == null || transformer == null) return;

        _grabbable.MaxGrabPoints = 2;
        _grabbable.InjectOptionalOneGrabTransformer(null);
        _grabbable.InjectOptionalTwoGrabTransformer(transformer);
        transformer.Initialize(_grabbable);
    }

    // ----- Gliding into place -----

    // A move the system makes rather than the hand: the piece is set to where
    // it is going, then shown sliding there from where it was. Anything that
    // asks where it stands gets where it is going.
    private Coroutine _glide;
    private PiecePose _glideTo;

    public PiecePose CommittedPose => _glide != null ? _glideTo : new PiecePose(transform);

    // Call after moving the piece to where it should end up.
    public void GlideFrom(PiecePose from, float duration)
    {
        StopGlide();
        if (duration <= 0f || !isActiveAndEnabled) return;

        _glideTo = new PiecePose(transform);
        Place(from);
        _glide = StartCoroutine(Glide(from, duration));
    }

    // Jumps to where the glide was going.
    public void CompleteGlide()
    {
        if (_glide == null) return;
        StopCoroutine(_glide);
        _glide = null;
        Place(_glideTo);
    }

    // Stops where it is. True when a glide was running.
    public bool StopGlide()
    {
        if (_glide == null) return false;
        StopCoroutine(_glide);
        _glide = null;
        return true;
    }

    // How far a glide carries the piece's furthest point, in metres: the
    // larger of how far it travels, turns at its edge, and grows at its edge.
    public static float GlideMeters(Transform root, float radius, PiecePose from, PiecePose to)
    {
        radius *= Mathf.Abs(root.lossyScale.x);
        float atScale = radius * Mathf.Max(Mathf.Abs(from.scale.x), Mathf.Abs(to.scale.x));

        float move = root.TransformVector(to.pos - from.pos).magnitude;
        float turn = Quaternion.Angle(from.rot, to.rot) * Mathf.Deg2Rad * atScale;
        float grow = Mathf.Abs(to.scale.x - from.scale.x) * radius;
        return Mathf.Max(move, Mathf.Max(turn, grow));
    }

    private void Place(PiecePose pose)
    {
        transform.localPosition = pose.pos;
        transform.localRotation = pose.rot;
        transform.localScale = pose.scale;
    }

    // A hand taking hold stops the glide where it is: the hand has it now.
    private IEnumerator Glide(PiecePose from, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            yield return null;
            if (IsGrabbed) { _glide = null; yield break; }

            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            transform.localPosition = Vector3.Lerp(from.pos, _glideTo.pos, k);
            transform.localRotation = Quaternion.Slerp(from.rot, _glideTo.rot, k);
            transform.localScale = Vector3.Lerp(from.scale, _glideTo.scale, k);
        }
        _glide = null;
    }
}
