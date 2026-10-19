using System.Globalization;
using System.Text;

namespace MapBuilder;

public static class Geometry
{
    /// <summary>Douglas–Peucker simplification that keeps both endpoints (closed loops are split in two).</summary>
    public static List<(double X, double Y)> Simplify(List<(double X, double Y)> pts, double epsilon)
    {
        if (pts.Count < 3) return pts;

        if (pts[0] == pts[^1])
        {
            var far = 1;
            for (var i = 1; i < pts.Count - 1; i++)
                if (Dist2(pts[i], pts[0]) > Dist2(pts[far], pts[0])) far = i;
            var a = Simplify(pts.Take(far + 1).ToList(), epsilon);
            var b = Simplify(pts.Skip(far).ToList(), epsilon);
            return a.Concat(b.Skip(1)).ToList();
        }

        var keep = new bool[pts.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, pts.Count - 1));
        while (stack.Count > 0)
        {
            var (s, e) = stack.Pop();
            double maxD = 0;
            var idx = -1;
            for (var i = s + 1; i < e; i++)
            {
                var d = SegmentDistance(pts[i], pts[s], pts[e]);
                if (d > maxD) { maxD = d; idx = i; }
            }
            if (idx < 0 || maxD <= epsilon) continue;
            keep[idx] = true;
            stack.Push((s, idx));
            stack.Push((idx, e));
        }
        return pts.Where((_, i) => keep[i]).ToList();
    }

    /// <summary>Chaikin corner cutting with fixed endpoints, so shared arcs still meet at junctions.</summary>
    public static List<(double X, double Y)> Smooth(List<(double X, double Y)> pts, int iterations)
    {
        for (var it = 0; it < iterations && pts.Count > 2; it++)
        {
            var result = new List<(double X, double Y)> { pts[0] };
            var last = pts.Count - 2;
            for (var i = 0; i <= last; i++)
            {
                var (p, q) = (pts[i], pts[i + 1]);
                if (i != 0) result.Add((0.75 * p.X + 0.25 * q.X, 0.75 * p.Y + 0.25 * q.Y));
                if (i != last) result.Add((0.25 * p.X + 0.75 * q.X, 0.25 * p.Y + 0.75 * q.Y));
            }
            result.Add(pts[^1]);
            pts = result;
        }
        return pts;
    }

    public static string ToSvgPath(List<List<(double X, double Y)>> rings)
    {
        var sb = new StringBuilder();
        foreach (var ring in rings)
        {
            for (var i = 0; i < ring.Count - 1; i++)
            {
                sb.Append(i == 0 ? 'M' : 'L');
                sb.Append(Fmt(ring[i].X)).Append(' ').Append(Fmt(ring[i].Y));
            }
            sb.Append('Z');
        }
        return sb.ToString();
    }

    private static string Fmt(double v) => Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture);

    private static double Dist2((double X, double Y) a, (double X, double Y) b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    private static double SegmentDistance((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len2 = dx * dx + dy * dy;
        if (len2 == 0) return Math.Sqrt(Dist2(p, a));
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        return Math.Sqrt(Dist2(p, (a.X + t * dx, a.Y + t * dy)));
    }
}
