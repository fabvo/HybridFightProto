using UnityEngine;
// Only pull in the InputSystem namespace when we'll actually compile against it.
// In "Both" mode (ENABLE_LEGACY + ENABLE_INPUT_SYSTEM) we use the legacy path, and
// having `using UnityEngine.InputSystem;` would cause name clashes.
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

public enum TiltState
{
    None,
    Block,
    AttackReady,
    FaceDown
}

/// <summary>
/// Reads the device accelerometer and classifies the phone's pose into a small set of tilt
/// states. The values are smoothed so jitter from your hand doesn't flicker the state.
///
/// Works under all three Active Input Handling settings:
///   - "Input Manager (Old)"           -> legacy UnityEngine.Input.acceleration
///   - "Both"                          -> legacy (preferred)
///   - "Input System Package (New)"    -> UnityEngine.InputSystem.Accelerometer
/// </summary>
public class TiltDetector : MonoBehaviour
{
    public TiltState State { get; private set; } = TiltState.None;
    public event System.Action<TiltState> OnStateChanged;

    [Tooltip("How dominant a single axis must be to count as that orientation. 0.7 ~= +/-45 deg tolerance.")]
    [SerializeField] float threshold = 0.7f;

    [Tooltip("Higher = snappier, lower = more smoothing.")]
    [SerializeField] float smoothing = 0.15f;

    Vector3 smoothedAccel;

    void Start()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        // The new Input System keeps the accelerometer disabled until you ask for it.
        if (Accelerometer.current != null && !Accelerometer.current.enabled)
            InputSystem.EnableDevice(Accelerometer.current);
#endif
    }

    Vector3 ReadAcceleration()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        // Phone upright, screen toward you: g ~= (0, -1,  0)
        // Phone flat,   screen up:          g ~= (0,  0, -1)
        // Phone flat,   screen down:        g ~= (0,  0,  1)
        return Input.acceleration;
#elif ENABLE_INPUT_SYSTEM
        return Accelerometer.current != null
            ? Accelerometer.current.acceleration.ReadValue()
            : Vector3.zero;
#else
        return Vector3.zero;
#endif
    }

    void Update()
    {
        smoothedAccel = Vector3.Lerp(smoothedAccel, ReadAcceleration(), smoothing);
        TiltState next = Determine(smoothedAccel);
        if (next != State)
        {
            State = next;
            OnStateChanged?.Invoke(State);
        }
    }

    TiltState Determine(Vector3 g)
    {
        if (g.y < -threshold) return TiltState.Block;
        if (g.z < -threshold) return TiltState.AttackReady;
        if (g.z >  threshold) return TiltState.FaceDown;
        return TiltState.None;
    }
}
