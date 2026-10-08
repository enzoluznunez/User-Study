using UnityEngine;

// Feeds ReadSheets from the hands' fingertips: the nearest bar is hovered,
// pressed and let go of.
[RequireComponent(typeof(ReadSheets))]
public class SheetTouch : PokeTouch<CreateCube>
{
    private ReadSheets _hub;

    private void Awake() => _hub = GetComponent<ReadSheets>();

    protected override bool Listening => _hub != null && _hub.Listening;

    protected override bool Inside(CreateCube cube, Vector3 tip) => PokeRaycast.Contains(cube.Collider, tip);

    protected override void Hover(PokeHit<CreateCube> hit, Vector3 tip, Vector3 wrist) =>
        _hub.Hover(ReadSheets.Describe(hit, tip, wrist));

    protected override void Select(PokeHit<CreateCube> hit, Vector3 tip, Vector3 wrist) =>
        _hub.Select(ReadSheets.Describe(hit, tip, wrist));

    protected override void Release(PokeHit<CreateCube> hit, Vector3 tip, Vector3 wrist) =>
        _hub.Release(ReadSheets.Describe(hit, tip, wrist));

    protected override void ReleaseAt(CreateCube cube) =>
        _hub.Release(ReadSheets.Describe(cube, cube.transform.position));

    protected override void Cleared() => _hub.Cleared();
}
