namespace MapBuilder;

/// <summary>A border segment between two labels (-1 = outside the country), from junction to junction.</summary>
public sealed class Arc
{
    public required List<(double X, double Y)> Points { get; set; }
    public required int Left { get; init; }
    public required int Right { get; init; }
    public required double RawLength { get; init; }
}

/// <summary>
/// Builds shared borders ("arcs") between labelled pixel regions, like TopoJSON does.
/// Each arc is simplified once and reused by both neighbours, so borders never gap or overlap.
/// Vertices live on pixel corners: (0..w, 0..h).
/// </summary>
public static class Topology
{
    public static List<Arc> BuildArcs(int[] labels, int w, int h)
    {
        int L(int x, int y) => x < 0 || y < 0 || x >= w || y >= h ? -1 : labels[y * w + x];

        // Horizontal edge (x,y)->(x+1,y) and vertical edge (x,y)->(x,y+1) exist where labels differ.
        bool HasH(int x, int y) => x >= 0 && x < w && y >= 0 && y <= h && L(x, y - 1) != L(x, y);
        bool HasV(int x, int y) => y >= 0 && y < h && x >= 0 && x <= w && L(x - 1, y) != L(x, y);

        var visitedH = new bool[(w + 1) * (h + 1)];
        var visitedV = new bool[(w + 1) * (h + 1)];
        int Idx(int x, int y) => y * (w + 1) + x;

        IEnumerable<(int Dx, int Dy)> Steps(int x, int y)
        {
            if (HasH(x, y)) yield return (1, 0);
            if (HasH(x - 1, y)) yield return (-1, 0);
            if (HasV(x, y)) yield return (0, 1);
            if (HasV(x, y - 1)) yield return (0, -1);
        }

        bool IsVisited(int x, int y, (int Dx, int Dy) s) => s switch
        {
            (1, 0) => visitedH[Idx(x, y)],
            (-1, 0) => visitedH[Idx(x - 1, y)],
            (0, 1) => visitedV[Idx(x, y)],
            _ => visitedV[Idx(x, y - 1)],
        };

        void MarkVisited(int x, int y, (int Dx, int Dy) s)
        {
            switch (s)
            {
                case (1, 0): visitedH[Idx(x, y)] = true; break;
                case (-1, 0): visitedH[Idx(x - 1, y)] = true; break;
                case (0, 1): visitedV[Idx(x, y)] = true; break;
                default: visitedV[Idx(x, y - 1)] = true; break;
            }
        }

        (int Left, int Right) Sides(int x, int y, (int Dx, int Dy) s) => s switch
        {
            (1, 0) => (L(x, y - 1), L(x, y)),
            (-1, 0) => (L(x - 1, y), L(x - 1, y - 1)),
            (0, 1) => (L(x, y), L(x - 1, y)),
            _ => (L(x - 1, y - 1), L(x, y - 1)),
        };

        var isJunction = new bool[(w + 1) * (h + 1)];
        for (var y = 0; y <= h; y++)
        for (var x = 0; x <= w; x++)
        {
            var degree = Steps(x, y).Count();
            if (degree == 0) continue;
            var distinct = new HashSet<int> { L(x - 1, y - 1), L(x, y - 1), L(x - 1, y), L(x, y) }.Count;
            isJunction[Idx(x, y)] = degree != 2 || distinct >= 3;
        }

        var arcs = new List<Arc>();

        void Trace(int sx, int sy, (int Dx, int Dy) first)
        {
            var (left, right) = Sides(sx, sy, first);
            var points = new List<(double, double)> { (sx, sy) };
            int x = sx, y = sy;
            var step = first;
            var length = 0;
            while (true)
            {
                MarkVisited(x, y, step);
                x += step.Dx; y += step.Dy;
                length++;
                points.Add((x, y));
                if (isJunction[Idx(x, y)]) break;
                var next = Steps(x, y).Where(s => !IsVisited(x, y, s)).ToList();
                if (next.Count == 0) break;
                step = next[0];
            }
            arcs.Add(new Arc { Points = points, Left = left, Right = right, RawLength = length });
        }

        for (var y = 0; y <= h; y++)
        for (var x = 0; x <= w; x++)
        {
            if (!isJunction[Idx(x, y)]) continue;
            foreach (var s in Steps(x, y).ToList())
                if (!IsVisited(x, y, s)) Trace(x, y, s);
        }

        // Closed loops without junctions (e.g. a province fully enclosed by another one).
        for (var y = 0; y <= h; y++)
        for (var x = 0; x <= w; x++)
        {
            foreach (var s in Steps(x, y).ToList())
            {
                if (IsVisited(x, y, s)) continue;
                isJunction[Idx(x, y)] = true;
                Trace(x, y, s);
            }
        }

        return arcs;
    }

    /// <summary>Joins the arcs around one label into closed rings (outer boundaries and holes).</summary>
    public static List<List<(double X, double Y)>> AssembleRings(List<Arc> arcs, int label)
    {
        static (long, long) Key((double X, double Y) p) => ((long)Math.Round(p.X * 1000), (long)Math.Round(p.Y * 1000));

        var oriented = arcs
            .Where(a => a.Left == label || a.Right == label)
            .Select(a => a.Left == label ? a.Points : Enumerable.Reverse(a.Points).ToList())
            .ToList();

        var byStart = oriented
            .Select((pts, i) => (pts, i))
            .GroupBy(t => Key(t.pts[0]))
            .ToDictionary(g => g.Key, g => g.Select(t => t.i).ToList());
        var used = new bool[oriented.Count];
        var rings = new List<List<(double X, double Y)>>();

        for (var i = 0; i < oriented.Count; i++)
        {
            if (used[i]) continue;
            used[i] = true;
            var ring = new List<(double X, double Y)>(oriented[i]);
            var startKey = Key(ring[0]);
            while (Key(ring[^1]) != startKey)
            {
                var candidates = byStart.GetValueOrDefault(Key(ring[^1]));
                var next = candidates?.FirstOrDefault(c => !used[c], -1) ?? -1;
                if (next < 0) throw new InvalidOperationException($"Open ring for label {label} at {ring[^1]}.");
                used[next] = true;
                ring.AddRange(oriented[next].Skip(1));
            }
            rings.Add(ring);
        }
        return rings;
    }
}
