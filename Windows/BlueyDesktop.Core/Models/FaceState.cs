namespace BlueyDesktop.Core.Models;

/// <summary>What Bluey's face should show right now.</summary>
/// <param name="GazeX">Where the eyes look horizontally: -1 left to 1 right.</param>
/// <param name="GazeY">Where the eyes look vertically: -1 up to 1 down.</param>
/// <param name="Mood">The expression.</param>
/// <param name="Talk">Voice level, 0 to 1. Drives the talking bounce.</param>
public sealed record FaceState(double GazeX = 0, double GazeY = 0, Mood Mood = Mood.Listening, double Talk = 0);
