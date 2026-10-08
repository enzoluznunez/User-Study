using System;
using UnityEngine;

// Where the fingertip is on one view, as events the tools listen to: hovering,
// pressing, letting go, and committing. A press commits when it is released
// over the same thing it started on, which is what a poke means. ReadSheets and
// ReadGraph are this hub for the sheet and the graph; TTouch is the component
// that feeds it from the hands.
public abstract class ReadHub<TReading, TTouch> : MonoBehaviour where TTouch : Component
{
    public event Action<TReading> OnHover;
    public event Action<TReading> OnSelect;
    public event Action<TReading> OnRelease;
    public event Action<TReading> OnCommit;
    public event Action OnCleared;

    public bool Listening => OnHover != null || OnSelect != null || OnRelease != null ||
        OnCommit != null || OnCleared != null;

    [Tooltip("Let a fingertip drive this view. Turn off only to take it out of play.")]
    public bool touchInput = true;

    private UnityEngine.Object _pressed;

    protected abstract bool IsValid(TReading reading);

    // What a press is on: the thing a release has to land on again to commit.
    protected abstract UnityEngine.Object PressTarget(TReading reading);

    private void Awake()
    {
        if (touchInput && GetComponent<TTouch>() == null) gameObject.AddComponent<TTouch>();
    }

    public void Hover(TReading reading)
    {
        if (Listening) OnHover?.Invoke(reading);
    }

    public void Select(TReading reading)
    {
        if (!Listening) return;
        _pressed = PressTarget(reading);
        OnSelect?.Invoke(reading);
    }

    public void Release(TReading reading)
    {
        if (!Listening) return;

        bool commits = IsValid(reading) && _pressed != null && _pressed == PressTarget(reading);
        _pressed = null;

        OnRelease?.Invoke(reading);
        if (commits) OnCommit?.Invoke(reading);
    }

    public void Cleared()
    {
        if (!Listening) return;
        _pressed = null;
        OnCleared?.Invoke();
    }
}
