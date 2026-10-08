using UnityEngine;

public static class CameraRig
{
    private static Transform _cached;

    public static Transform MainTransform
    {
        get
        {
            if (_cached == null)
            {
                Camera cam = Camera.main;
                if (cam != null) _cached = cam.transform;
            }
            return _cached;
        }
    }

    public const float DefaultMaxPitch = 75f;

    public static Vector3 Flatten(Vector3 v, Vector3 fallback)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : fallback;
    }

    public static bool TryFaceViewer(Vector3 position, float extraPitch, bool adaptPitch,
        float maxPitch, out Quaternion rotation)
    {
        rotation = Quaternion.identity;

        Transform cam = MainTransform;
        if (cam == null) return false;

        Vector3 toViewer = cam.position - position;
        Vector3 flat = toViewer;
        flat.y = 0f;
        float horizontal = flat.magnitude;
        if (horizontal < 1e-4f) return false;

        float tilt = extraPitch;
        if (adaptPitch)
        {
            float elevation = Mathf.Atan2(toViewer.y, horizontal) * Mathf.Rad2Deg;
            tilt = Mathf.Clamp(elevation + extraPitch, -maxPitch, maxPitch);
        }

        rotation = Quaternion.LookRotation(-flat / horizontal, Vector3.up)
                   * Quaternion.Euler(tilt, 0f, 0f);
        return true;
    }

    public static Vector3 FlatForward
    {
        get
        {
            Transform cam = MainTransform;
            return cam != null ? Flatten(cam.forward, Vector3.forward) : Vector3.forward;
        }
    }

    // Whether the headset knows where the eyes are yet: before it does, the
    // camera sits at the tracking origin and anything placed from it lands on
    // the floor.
    public static bool HeadPoseReady()
    {
        if (!OVRManager.OVRManagerinitialized) return true;
        return OVRPlugin.userPresent && OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.EyeCenter);
    }

    // The viewer's eye position and the way they face, levelled to the floor.
    public static bool TryGetBasis(out Vector3 position, out Quaternion yaw)
    {
        position = Vector3.zero;
        yaw = Quaternion.identity;

        if (!HeadPoseReady()) return false;

        Transform cam = MainTransform;
        if (cam == null) return false;

        position = cam.position;

        Vector3 flat = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
        if (flat.sqrMagnitude < 1e-6f) flat = Vector3.ProjectOnPlane(cam.up, Vector3.up);
        if (flat.sqrMagnitude < 1e-6f) return false;

        yaw = Quaternion.LookRotation(flat.normalized, Vector3.up);
        return true;
    }
}
