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

    private const double MaxStep = 1.0 / 20;     // a stall must not fling the springs around
    private const double BlinkLength = 0.15;     // seconds
    private const double DoubleBlinkChance = 0.25;

    private readonly Random _random = new();
    private readonly Spring _gazeX = new(0, stiffness: 260, damping: 0.82);
    private readonly Spring _gazeY = new(0, stiffness: 260, damping: 0.82);
    private readonly Spring _lean = new(0, stiffness: 60, damping: 0.8);
    private readonly Spring _hop = new(0, stiffness: 260, damping: 0.35);

    private double? _last;
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
    public Frame Step(double now)
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
        _gazeX.Step(_wanderX + _jitterX, dt);
        _gazeY.Step(_wanderY + _jitterY, dt);
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

    private double Between(double low, double high) => low + _random.NextDouble() * (high - low);
}
