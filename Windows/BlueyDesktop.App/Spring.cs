namespace BlueyDesktop.App;

/// <summary>
/// A tiny damped spring, so every part of the face moves with a little life.
/// Ported from the <c>Spring</c> in iOS/Sources/FaceView.swift.
/// </summary>
internal sealed class Spring
{
    private readonly double _stiffness;
    private readonly double _damping;  // 1 = no overshoot, lower = bouncier

    public Spring(double value, double stiffness = 200, double damping = 0.7)
    {
        Value = value;
        _stiffness = stiffness;
        _damping = damping;
    }

    public double Value { get; private set; }

    /// <summary>Add to this to kick the spring (a hop, a flinch).</summary>
    public double Velocity { get; set; }

    public void Step(double target, double dt)
    {
        var c = 2 * Math.Sqrt(_stiffness) * _damping;
        Velocity += (_stiffness * (target - Value) - c * Velocity) * dt;
        Value += Velocity * dt;
    }
}
