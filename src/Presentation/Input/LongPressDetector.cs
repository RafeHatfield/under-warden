using Godot;

namespace UnderWarden.Presentation.Input;

/// <summary>
/// Detects long-press (mobile) and stationary hover (desktop) gestures.
///
/// Mobile: touch down starts a 0.5s timer; if still held when threshold passes → emit LongPressDetected.
///   Touch released before threshold → cancel.
///
/// Desktop: mouse motion resets the timer. After 0.3s of no movement → emit LongPressDetected.
///   Used for "hover to inspect" on PC builds.
///
/// Emits screen-space position so the caller can convert to grid via IsometricMapper.
/// </summary>
public sealed partial class LongPressDetector : Node
{
    /// <summary>Seconds a touch must be held before firing (mobile).</summary>
    private const float TouchThreshold = 0.5f;

    /// <summary>Seconds mouse must be stationary before firing (desktop).</summary>
    private const float HoverThreshold = 0.3f;

    [Signal]
    public delegate void LongPressDetectedEventHandler(Vector2 screenPosition);

    // Touch state
    private bool _touching;
    private Vector2 _touchPosition;
    private float _touchHeldTime;

    // Desktop hover state
    private Vector2 _mousePosition;
    private float _mouseStationaryTime;
    private bool _hoverFired;

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventScreenTouch touch)
        {
            if (touch.Pressed)
            {
                // Touch started — begin hold timer
                _touching = true;
                _touchPosition = touch.Position;
                _touchHeldTime = 0f;
            }
            else
            {
                // Touch released — cancel
                _touching = false;
            }
        }
        else if (@event is InputEventMouseMotion motion)
        {
            // Any mouse movement resets the hover timer
            if (_mousePosition.DistanceTo(motion.GlobalPosition) > 2f)
            {
                _mousePosition = motion.GlobalPosition;
                _mouseStationaryTime = 0f;
                _hoverFired = false;
            }
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // Touch: accumulate held time and fire once threshold is reached
        if (_touching)
        {
            _touchHeldTime += dt;
            if (_touchHeldTime >= TouchThreshold)
            {
                _touching = false; // prevent re-firing while held
                EmitSignal(SignalName.LongPressDetected, _touchPosition);
            }
        }

        // Desktop: accumulate stationary time and fire once (per stop)
        if (!_hoverFired && _mousePosition != Vector2.Zero)
        {
            _mouseStationaryTime += dt;
            if (_mouseStationaryTime >= HoverThreshold)
            {
                _hoverFired = true;
                EmitSignal(SignalName.LongPressDetected, _mousePosition);
            }
        }
    }

    /// <summary>
    /// Cancel any in-progress long press detection.
    /// Call when a normal tap fires so the hover timer doesn't also fire.
    /// </summary>
    public void Cancel()
    {
        _touching = false;
        _touchHeldTime = 0f;
        _mouseStationaryTime = 0f;
        _hoverFired = false;
    }
}
