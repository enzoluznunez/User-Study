using System;
using UnityEngine;

// The nearest thing of one kind to a fingertip: a bar of the sheet or a node of
// the graph. Measured to the collider's surface, so a fingertip inside it is at
// distance zero.
public struct PokeHit<T> where T : Component
{
    public T target;
    public Vector3 point;
    public Vector3 normal;
    public float distance;
}

public static class PokeRaycast
{
    private const int MaxBuffer = 4096;

    private static Collider[] _overlaps = new Collider[64];

    public static bool Nearest<T>(Vector3 point, float radius, Func<T, bool> accept, out PokeHit<T> hit)
        where T : Component
    {
        hit = default;
        if (radius <= 0f) return false;

        int count = Overlap(point, radius);
        float best = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider candidate = _overlaps[i];
            if (candidate == null || !candidate.TryGetComponent(out T target)) continue;
            if (accept != null && !accept(target)) continue;

            Vector3 closest = candidate.ClosestPoint(point);
            float distance = Vector3.Distance(closest, point);
            if (distance >= best) continue;

            best = distance;
            hit = new PokeHit<T>
            {
                target = target,
                point = closest,
                normal = distance > 1e-5f ? (point - closest).normalized : Vector3.up,
                distance = distance
            };
        }

        return hit.target != null;
    }

    public static bool Contains(Collider collider, Vector3 point)
    {
        if (collider == null || !collider.enabled) return false;
        return (collider.ClosestPoint(point) - point).sqrMagnitude <= 1e-8f;
    }

    private static int Overlap(Vector3 point, float radius)
    {
        while (true)
        {
            int count = Physics.OverlapSphereNonAlloc(point, radius, _overlaps,
                ~0, QueryTriggerInteraction.Collide);

            if (count < _overlaps.Length || _overlaps.Length >= MaxBuffer) return count;
            _overlaps = new Collider[Mathf.Min(_overlaps.Length * 2, MaxBuffer)];
        }
    }
}
