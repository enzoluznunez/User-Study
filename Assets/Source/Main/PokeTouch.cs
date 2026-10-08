using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.Input;

// Turns the hands' poke interactors into hover, press and release on the
// nearest target of one kind: a bar of the sheet, or a node of the graph.
// One fingertip drives at a time. The target answers once the fingertip comes
// within hoverEnter of it, is pressed when the fingertip reaches it, and lets
// go once the fingertip draws back past hoverExit, so a hand trembling at the
// edge does not flicker between states.
public abstract class PokeTouch<T> : MonoBehaviour where T : Component
{
    [Tooltip("How far past the fingertip a target starts responding, in metres.")]
    public float hoverEnter = 0.02f;

    [Tooltip("How far the fingertip must withdraw before the target stops responding. Keep above hoverEnter.")]
    public float hoverExit = 0.05f;

    [Tooltip("Seconds between sweeps for poke interactors that appear after start.")]
    public float rescanSeconds = 2f;

    [Tooltip("Furthest a fingertip can credibly be from the head, in metres. Guards against parked interactors.")]
    public float maxReachFromHead = 1.2f;

    private struct Source
    {
        public PokeInteractor poke;
        public IHand hand;
    }

    private readonly List<Source> _pokes = new List<Source>();
    private float _nextScan;

    private PokeInteractor _driver;
    private T _target;
    private bool _selected;

    // Whether anyone is listening; nothing is tracked while nobody is.
    protected abstract bool Listening { get; }

    // Whether a target can be poked right now.
    protected virtual bool Accept(T target) => true;

    // Whether the fingertip is inside the target, which presses it as surely
    // as touching its surface does.
    protected virtual bool Inside(T target, Vector3 tip) => false;

    protected abstract void Hover(PokeHit<T> hit, Vector3 tip, Vector3 wrist);
    protected abstract void Select(PokeHit<T> hit, Vector3 tip, Vector3 wrist);
    protected abstract void Release(PokeHit<T> hit, Vector3 tip, Vector3 wrist);

    // A press cut short with no fingertip to report: released where it stood.
    protected abstract void ReleaseAt(T target);
    protected abstract void Cleared();

    protected virtual void OnDisable() => Drop();

    private void Update()
    {
        if (!Listening) { Drop(); return; }

        Rescan();

        if (_driver != null && !Track(_driver)) Drop();
        if (_driver != null) return;

        for (int i = 0; i < _pokes.Count; i++)
        {
            Source source = _pokes[i];
            if (!Live(source) || !Acquire(source)) continue;
            _driver = source.poke;
            return;
        }
    }

    private void Rescan()
    {
        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + Mathf.Max(rescanSeconds, 0.25f);

        _pokes.Clear();
        PokeInteractor[] found = FindObjectsByType<PokeInteractor>(FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
        {
            IHand hand = found[i].GetComponentInParent<IHand>();
            if (hand != null) _pokes.Add(new Source { poke = found[i], hand = hand });
        }
    }

    private bool Live(Source source)
    {
        PokeInteractor poke = source.poke;
        if (poke == null || !poke.isActiveAndEnabled) return false;
        if (source.hand == null || !source.hand.IsTrackedDataValid) return false;

        Transform head = CameraRig.MainTransform;
        if (head == null) return false;

        return (poke.Origin - head.position).sqrMagnitude <= maxReachFromHead * maxReachFromHead;
    }

    private Source Current()
    {
        for (int i = 0; i < _pokes.Count; i++)
            if (_pokes[i].poke == _driver) return _pokes[i];
        return default;
    }

    private static Vector3 WristOf(Source source, Vector3 tip) =>
        source.hand != null && source.hand.GetJointPose(HandJointId.HandWristRoot, out Pose wrist)
            ? wrist.position
            : tip;

    private System.Func<T, bool> _accept;

    private bool Nearest(Vector3 tip, float reach, out PokeHit<T> hit) =>
        PokeRaycast.Nearest(tip, reach, _accept ??= Accept, out hit);

    private bool Acquire(Source source)
    {
        Vector3 tip = source.poke.Origin;
        float reach = Mathf.Max(source.poke.Radius, 0f) + Mathf.Max(hoverEnter, 0f);
        if (!Nearest(tip, reach, out PokeHit<T> hit)) return false;

        _target = hit.target;
        _selected = false;
        Hover(hit, tip, WristOf(source, tip));
        return true;
    }

    private bool Track(PokeInteractor poke)
    {
        Source source = Current();
        if (!Live(source)) return false;

        Vector3 tip = poke.Origin;
        float radius = Mathf.Max(poke.Radius, 0f);
        float reach = radius + Mathf.Max(hoverExit, hoverEnter);
        if (!Nearest(tip, reach, out PokeHit<T> hit)) return false;

        Vector3 wrist = WristOf(source, tip);
        _target = hit.target;

        if (_selected)
        {
            if (hit.distance <= radius + Mathf.Max(hoverExit, 0f))
            {
                Hover(hit, tip, wrist);
                return true;
            }
            _selected = false;
            Release(hit, tip, wrist);
            return true;
        }

        if (hit.distance <= radius || Inside(hit.target, tip))
        {
            _selected = true;
            Select(hit, tip, wrist);
            return true;
        }

        Hover(hit, tip, wrist);
        return true;
    }

    private void Drop()
    {
        if (_driver == null && _target == null && !_selected) return;

        bool wasSelected = _selected;
        T target = _target;

        _driver = null;
        _target = null;
        _selected = false;

        if (wasSelected && target != null) ReleaseAt(target);
        Cleared();
    }
}
