namespace BlueyDesktop.App;

/// <summary>
/// What he does when nothing is going on: eyes darting around, blinks (sometimes doubled), breathing,
/// the occasional happy hop, and leaning toward where he looks. A trimmed port of <c>FaceAnimator</c>
/// in iOS/Sources/FaceView.swift, without the moods and the Mac's commands for now.
/// </summary>
internal sealed class IdleAnimator
{
    /// <summary>One frame of motion. Gaze is in -1…1 units; Hop is in design px (up is +).</summary>
    public readonly record struct Frame(double GazeX, double GazeY, double Closed, double Breathe, double Hop, double Lean);

    /// <summary>Where the real mouse cursor is, measured from between his eyes, in device-independent px (screen space, y down).</summary>
    public readonly record struct CursorOffset(double X, double Y);

    private const double MaxStep = 1.0 / 20;     // a stall must not fling the springs around
    private const double BlinkLength = 0.15;     // seconds
    private const double DoubleBlinkChance = 0.25;

    // Following the cursor. It biases his gaze rather than replacing the wandering, so he never looks locked on.
    private const double FollowNear = 450;       // px: this close or closer, he pays full attention
    private const double FollowFar = 1100;       // px: farther than this, he ignores the cursor
    private const double FullGlance = 140;       // px: the pupils reach the rim once the cursor is this far from his eyes
    private const double CursorIdleAfter = 6;    // s without the cursor moving, then he loses interest
    private const double CursorMovedThreshold = 2;  // px of change that counts as the cursor moving
    private const double MaxCursorWeight = 0.9;  // the most the cursor can pull the gaze; the rest stays wander
    private const double AttentionRate = 4;      // per second: how fast his interest rises and fades

    private readonly Random _random = new();
    private readonly Spring _gazeX = new(0, stiffness: 260, damping: 0.82);
    private readonly Spring _gazeY = new(0, stiffness: 260, damping: 0.82);
    private readonly Spring _lean = new(0, stiffness: 60, damping: 0.8);
    private readonly Spring _hop = new(0, stiffness: 260, damping: 0.35);

    private double? _last;
    private double _attention;                   // 0 = idle wander, 1 = watching the cursor
    private CursorOffset? _lastCursor;
    private double _lastCursorMove = double.NegativeInfinity;
    private double _nextSaccade;
    private double _wanderX;
    private double _wanderY = -0.3;
    private double _nextJitter;
    private double _jitterX;
    private double _jitterY;
    private double _nextHop = 6;
    private double _nextBlink = 2;
    private double _blinkStart = -1;
    private bool _doubleBlink;

    /// <param name="now">Seconds on any steady clock.</param>
    /// <param name="cursor">Where the mouse cursor is relative to his eyes, or null if it can't be read.</param>
    public Frame Step(double now, CursorOffset? cursor = null)
    {
        var dt = Math.Min(now - (_last ?? now), MaxStep);
        _last = now;

        // Where to look, plus tiny darting movements so the eyes never sit dead still.
        if (now > _nextSaccade)
        {
            _wanderX = Between(-0.9, 0.9);
            _wanderY = Between(-0.9, 0.4);
            _nextSaccade = now + Between(0.6, 2.2);
        }
        if (now > _nextJitter)
        {
            _jitterX = Between(-0.015, 0.015);
            _jitterY = Between(-0.015, 0.015);
            _nextJitter = now + Between(0.5, 1.4);
        }
        var (lookX, lookY) = GazeTarget(now, dt, cursor);
        _gazeX.Step(lookX + _jitterX, dt);
        _gazeY.Step(lookY + _jitterY, dt);
        _lean.Step(-_gazeX.Value * 0.045, dt);

        // An occasional happy little hop.
        if (now > _nextHop)
        {
            _hop.Velocity += Between(180, 320);
            _nextHop = now + Between(7, 14);
        }
        _hop.Step(0, dt);

        // Blinks, sometimes doubled.
        if (now > _nextBlink)
        {
            _blinkStart = now;
            _doubleBlink = _random.NextDouble() < DoubleBlinkChance;
            _nextBlink = now + Between(2.0, 5.0);
        }
        var p = (now - _blinkStart) / BlinkLength;
        var closed = p is >= 0 and <= 1 ? Math.Sin(Math.PI * p) : 0;
        if (_doubleBlink && p is >= 1.3 and <= 2.3) closed = Math.Sin(Math.PI * (p - 1.3));

        return new Frame(_gazeX.Value, _gazeY.Value, closed, Math.Sin(now * 1.8), _hop.Value, _lean.Value);
    }

    /// <summary>
    /// Where to look: the idle wander, pulled toward the cursor while it is near and moving.
    /// Interest fades in and out smoothly, so he never snaps between the two.
    /// </summary>
    private (double X, double Y) GazeTarget(double now, double dt, CursorOffset? cursor)
    {
        double wanted = 0, cursorX = 0, cursorY = 0;
        if (cursor is { } c)
        {
            if (_lastCursor is { } last && double.Hypot(c.X - last.X, c.Y - last.Y) > CursorMovedThreshold) _lastCursorMove = now;
            _lastCursor = c;

            var distance = double.Hypot(c.X, c.Y);
            var near = Math.Clamp((FollowFar - distance) / (FollowFar - FollowNear), 0, 1);
            wanted = now - _lastCursorMove < CursorIdleAfter ? near : 0;
            if (distance > 0)
            {
                // Looking straight at something close by is a small glance, not a hard sideways stare.
                var glance = Math.Min(1, distance / FullGlance);
                cursorX = c.X / distance * glance;
                cursorY = c.Y / distance * glance;
            }
        }
        else
        {
            _lastCursor = null;
        }

        _attention += (wanted - _attention) * Math.Min(1, dt * AttentionRate);
        var weight = _attention * MaxCursorWeight;
        return (_wanderX + (cursorX - _wanderX) * weight, _wanderY + (cursorY - _wanderY) * weight);
    }

    private double Between(double low, double high) => low + _random.NextDouble() * (high - low);
}
