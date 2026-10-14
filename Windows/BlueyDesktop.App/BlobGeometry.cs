using System.Windows;
using System.Windows.Media;

namespace BlueyDesktop.App;

/// <summary>The blob outline, ported from <c>BlobShape.path</c> in iOS/Sources/BlobShape.swift.</summary>
internal static class BlobGeometry
{
    private const double Quarter = 0.5523;  // cubic approximation of a quarter ellipse

    /// <summary>CSS <c>border-radius: 52% 48% 46% 54% / 58% 56% 44% 42%</c> drawn inside <paramref name="r"/>.</summary>
    public static Geometry Create(Rect r)
    {
        double w = r.Width, h = r.Height, k = 1 - Quarter;
        // Horizontal and vertical radii for top-left, top-right, bottom-right, bottom-left.
        var tl = new Size(0.52 * w, 0.58 * h);
        var tr = new Size(0.48 * w, 0.56 * h);
        var br = new Size(0.46 * w, 0.44 * h);
        var bl = new Size(0.54 * w, 0.42 * h);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(r.Left + tl.Width, r.Top), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(r.Right - tr.Width, r.Top), isStroked: true, isSmoothJoin: false);
            ctx.BezierTo(new Point(r.Right - tr.Width * k, r.Top), new Point(r.Right, r.Top + tr.Height * k),
                         new Point(r.Right, r.Top + tr.Height), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(r.Right, r.Bottom - br.Height), isStroked: true, isSmoothJoin: false);
            ctx.BezierTo(new Point(r.Right, r.Bottom - br.Height * k), new Point(r.Right - br.Width * k, r.Bottom),
                         new Point(r.Right - br.Width, r.Bottom), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(r.Left + bl.Width, r.Bottom), isStroked: true, isSmoothJoin: false);
            ctx.BezierTo(new Point(r.Left + bl.Width * k, r.Bottom), new Point(r.Left, r.Bottom - bl.Height * k),
                         new Point(r.Left, r.Bottom - bl.Height), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(r.Left, r.Top + tl.Height), isStroked: true, isSmoothJoin: false);
            ctx.BezierTo(new Point(r.Left, r.Top + tl.Height * k), new Point(r.Left + tl.Width * k, r.Top),
                         new Point(r.Left + tl.Width, r.Top), isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        return geometry;
    }
}
