using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BlueyDesktop.App;

/// <summary>
/// Bluey himself: the blob with his face, idling. The art is in BlueyCharacter.xaml; this moves it every frame.
/// </summary>
public partial class BlueyCharacter : UserControl
{
    private const double BreatheDepth = 3;       // design px
    private const double HopPerStretch = 900;    // design px of hop for a full stretch
    private const double MaxStretch = 0.03;
    private const double MinEyeOpen = 0.06;      // a blink never closes all the way
    // About 30 fps is plenty for an idle character. WPF raises the render callback at the monitor's refresh
    // rate (maybe 144 Hz) and redrawing a transparent window each time is CPU work he does not need.
    // A little under 1/30 s so a 60 Hz display still lands on every second frame.
    private static readonly TimeSpan FrameInterval = TimeSpan.FromSeconds(0.028);

    private static readonly Rect BodyBounds = new(12, 44, 820, 700);
    /// <summary>Halfway between his eyes, in the stage's design space. The cursor is measured from here.</summary>
    private static readonly Point EyesMidpoint = new(422, 201);

    private readonly IdleAnimator _animator = new();
    private readonly Stopwatch _clock = new();
    private readonly Eye _leftEye = new(new Point(292, 201), side: -1);
    private readonly Eye _rightEye = new(new Point(552, 201), side: 1);
    private TimeSpan _lastStep = TimeSpan.Zero;

    public BlueyCharacter()
    {
        InitializeComponent();

        var blob = BlobGeometry.Create(BodyBounds);
        Skin.Data = blob;
        Shine.Data = blob;
        Eyes.Children.Add(_leftEye.Root);
        Eyes.Children.Add(_rightEye.Root);

        Loaded += (_, _) =>
        {
            _clock.Start();
            CompositionTarget.Rendering += OnRendering;
        };
        Unloaded += (_, _) =>
        {
            _clock.Stop();
            CompositionTarget.Rendering -= OnRendering;
        };
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Also skips the repeat calls WPF can make within one frame (same RenderingTime).
        var renderingTime = ((RenderingEventArgs)e).RenderingTime;
        if (renderingTime - _lastStep < FrameInterval) return;
        _lastStep = renderingTime;

        var frame = _animator.Step(_clock.Elapsed.TotalSeconds, ReadCursorOffset());

        // Whole-body motion: breathing, hops, and leaning toward where he looks.
        var stretch = 1 + Math.Clamp(frame.Hop / HopPerStretch, -MaxStretch, MaxStretch);
        BodyStretch.ScaleX = 2 - stretch;
        BodyStretch.ScaleY = stretch;
        BodyLean.Angle = frame.Lean * 180 / Math.PI;
        BodyShift.Y = frame.Breathe * BreatheDepth - frame.Hop;

        var open = Math.Max(MinEyeOpen, 1 - frame.Closed);
        _leftEye.Update(frame.GazeX, frame.GazeY, open);
        _rightEye.Update(frame.GazeX, frame.GazeY, open);
    }

    /// <summary>The real mouse cursor relative to his eyes, in device-independent px, or null if it can't be read.</summary>
    private IdleAnimator.CursorOffset? ReadCursorOffset()
    {
        // The cursor can be unreadable (secure desktop, locked screen); he just goes back to wandering then.
        if (!NativeCursor.TryGet(out var cursor) || PresentationSource.FromVisual(Stage) is null) return null;

        // PointToScreen and GetCursorPos are both in device pixels; Stage doesn't move with his bobbing.
        var eyes = Stage.PointToScreen(EyesMidpoint);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        return new IdleAnimator.CursorOffset((cursor.X - eyes.X) / scale, (cursor.Y - eyes.Y) / scale);
    }

    private static class NativeCursor
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct CursorPoint
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out CursorPoint point);

        public static bool TryGet(out CursorPoint point) => GetCursorPos(out point);
    }

    /// <summary>
    /// One eye: a white disc with a pupil and two catchlights. It blinks by squashing the clip that shapes it,
    /// so the pupil is cut off by the lids instead of floating over them.
    /// </summary>
    private sealed class Eye
    {
        private const double Radius = 95;
        private const double PupilRadius = 46;
        private const double Reach = 50;          // how far the pupil travels from the middle
        private const double ConvergeInset = 3;   // eyes turn in a little toward each other

        private static readonly Brush Ink = Frozen(new SolidColorBrush(Color.FromRgb(0x17, 0x15, 0x1F)));

        private readonly EllipseGeometry _clip;
        private readonly TranslateTransform _pupil = new();
        private readonly Point _center;
        private readonly double _side;

        public Eye(Point center, double side)
        {
            _center = center;
            _side = side;
            _clip = new EllipseGeometry(center, Radius, Radius);

            var white = new Rectangle { Width = Radius * 2, Height = Radius * 2, Fill = Brushes.White };
            Canvas.SetLeft(white, center.X - Radius);
            Canvas.SetTop(white, center.Y - Radius);

            // The pupil and its two catchlights, drawn around their own origin and moved together by _pupil.
            var pupil = new Canvas { RenderTransform = _pupil };
            pupil.Children.Add(Disc(0, 0, PupilRadius, Ink, 1));
            pupil.Children.Add(Disc(-0.30 * PupilRadius, -0.44 * PupilRadius, 0.28 * PupilRadius, Brushes.White, 1));
            pupil.Children.Add(Disc(0.40 * PupilRadius, 0.34 * PupilRadius, 0.12 * PupilRadius, Brushes.White, 0.85));

            Root = new Canvas { Clip = _clip };
            Root.Children.Add(white);
            Root.Children.Add(pupil);
        }

        public Canvas Root { get; }

        /// <param name="open">1 wide open, near 0 shut.</param>
        public void Update(double gazeX, double gazeY, double open)
        {
            _clip.RadiusY = Radius * open;

            var length = Math.Sqrt(gazeX * gazeX + gazeY * gazeY);
            if (length > 1)
            {
                gazeX /= length;
                gazeY /= length;
            }
            _pupil.X = _center.X + gazeX * Reach - _side * ConvergeInset;
            _pupil.Y = _center.Y + gazeY * Reach * open;
        }

        private static Ellipse Disc(double centerX, double centerY, double radius, Brush fill, double opacity)
        {
            var disc = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = fill, Opacity = opacity };
            Canvas.SetLeft(disc, centerX - radius);
            Canvas.SetTop(disc, centerY - radius);
            return disc;
        }

        private static Brush Frozen(Brush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}
