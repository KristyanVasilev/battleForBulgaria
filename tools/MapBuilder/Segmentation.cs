namespace MapBuilder;

/// <summary>
/// Splits the reference image into one pixel label per province.
/// Province fills are saturated colours separated by thin white borders and overlaid with black text,
/// so we flood-fill similar saturated colours, then grow the regions over borders/text.
/// </summary>
public static class Segmentation
{
    private const int MinRegionArea = 400;
    private const int BorderGrowth = 3;
    private const int MinPieceArea = 150;
    private const int MergeSearchRadius = 12;

    public static int[] LabelRegions(PngDecoder.Image img, ProvinceSeed[] seeds, out ProvinceSeed[] labelToProvince)
    {
        int w = img.Width, h = img.Height;
        var component = new int[w * h];
        Array.Fill(component, -1);

        // 1) Flood fill saturated, colour-similar pixels into components.
        var components = new List<(int Area, int Seed)>();
        var stack = new Stack<int>();
        for (var start = 0; start < w * h; start++)
        {
            if (component[start] != -1 || !IsColored(img, start % w, start / w)) continue;

            var id = components.Count;
            var seedColor = img.Get(start % w, start / w);
            var area = 0;
            component[start] = id;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                area++;
                int x = p % w, y = p / w;
                var c = img.Get(x, y);
                foreach (var (nx, ny) in Neighbours4(x, y, w, h))
                {
                    var n = ny * w + nx;
                    if (component[n] != -1 || !IsColored(img, nx, ny)) continue;
                    var nc = img.Get(nx, ny);
                    if (Distance(nc, c) > 30 || Distance(nc, seedColor) > 70) continue;
                    component[n] = id;
                    stack.Push(n);
                }
            }
            components.Add((area, start));
        }

        // 2) Keep big components and match them to provinces by seed point.
        var labels = new int[w * h];
        Array.Fill(labels, -1);
        var big = components.Select((c, i) => (c.Area, Index: i)).Where(c => c.Area >= MinRegionArea).ToList();
        Console.WriteLine($"Colour components >= {MinRegionArea}px: {big.Count}");

        if (Environment.GetEnvironmentVariable("MAPBUILDER_DEBUG") == "1")
        {
            foreach (var s in seeds)
            {
                var c = component[s.SeedY * w + s.SeedX];
                Console.WriteLine($"  seed {s.Name,-16} rgb={img.Get(s.SeedX, s.SeedY)} comp={c} area={(c >= 0 ? components[c].Area : 0)}");
            }
            foreach (var (area, index) in big)
            {
                var (sx, sy) = (components[index].Seed % w, components[index].Seed / w);
                Console.WriteLine($"  comp {index} area={area} first=({sx},{sy}) rgb={img.Get(sx, sy)}");
            }
        }

        var componentToLabel = new Dictionary<int, int>();
        for (var label = 0; label < seeds.Length; label++)
        {
            var s = seeds[label];
            var comp = component[s.SeedY * w + s.SeedX];
            if (comp < 0 || components[comp].Area < MinRegionArea)
                throw new InvalidOperationException($"Seed for {s.Name} ({s.SeedX},{s.SeedY}) is not inside a province fill.");
            if (!componentToLabel.TryAdd(comp, label))
                throw new InvalidOperationException($"{s.Name} and {seeds[componentToLabel[comp]].Name} share a region.");
        }

        // Text labels can cut a province fill into pieces: attach each leftover piece to the
        // nearby matched region with the most similar colour.
        var pieces = components.Select((c, i) => (c.Area, Index: i))
            .Where(c => c.Area >= MinPieceArea && !componentToLabel.ContainsKey(c.Index))
            .ToList();
        foreach (var piece in pieces)
        {
            var target = NearestSimilarComponent(img, component, componentToLabel, piece.Index, w, h);
            var (x, y) = (components[piece.Index].Seed % w, components[piece.Index].Seed / w);
            if (target >= 0)
                componentToLabel[piece.Index] = componentToLabel[target];
            else if (piece.Area >= MinRegionArea)
                Console.WriteLine($"  warning: unmatched region of {piece.Area}px near ({x},{y}) ignored");
        }

        for (var p = 0; p < w * h; p++)
            if (component[p] >= 0 && componentToLabel.TryGetValue(component[p], out var l))
                labels[p] = l;

        // 3) Grow labels a few pixels to swallow white borders and the outer outline.
        Grow(labels, w, h, maxDepth: BorderGrowth, allowed: _ => true);

        // 4) Anything unlabelled that cannot reach the image edge is inside the country (text, borders): fill it.
        var outside = new bool[w * h];
        var queue = new Queue<int>();
        for (var x = 0; x < w; x++) { Enqueue(x, 0); Enqueue(x, h - 1); }
        for (var y = 0; y < h; y++) { Enqueue(0, y); Enqueue(w - 1, y); }
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var (nx, ny) in Neighbours4(p % w, p / w, w, h)) Enqueue(nx, ny);
        }
        Grow(labels, w, h, maxDepth: int.MaxValue, allowed: p => !outside[p]);

        labelToProvince = seeds;
        return labels;

        void Enqueue(int x, int y)
        {
            var p = y * w + x;
            if (outside[p] || labels[p] >= 0) return;
            outside[p] = true;
            queue.Enqueue(p);
        }
    }

    /// <summary>For each label, the interior pixel farthest from its border (good spot for a name).</summary>
    public static (double X, double Y)[] LabelAnchors(int[] labels, int w, int h, int labelCount)
    {
        var dist = new int[w * h];
        Array.Fill(dist, -1);
        var queue = new Queue<int>();
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var p = y * w + x;
            if (labels[p] < 0) continue;
            var onBorder = x == 0 || y == 0 || x == w - 1 || y == h - 1
                           || Neighbours4(x, y, w, h).Any(n => labels[n.Y * w + n.X] != labels[p]);
            if (!onBorder) continue;
            dist[p] = 0;
            queue.Enqueue(p);
        }
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var (nx, ny) in Neighbours4(p % w, p / w, w, h))
            {
                var n = ny * w + nx;
                if (dist[n] != -1 || labels[n] != labels[p]) continue;
                dist[n] = dist[p] + 1;
                queue.Enqueue(n);
            }
        }

        var best = new (int Dist, int P)[labelCount];
        for (var p = 0; p < w * h; p++)
            if (labels[p] >= 0 && dist[p] > best[labels[p]].Dist)
                best[labels[p]] = (dist[p], p);

        return best.Select(b => (b.P % w + 0.5, b.P / w + 0.5)).ToArray();
    }

    public static (int MinX, int MinY, int MaxX, int MaxY) Bounds(int[] labels, int w, int h)
    {
        int minX = w, minY = h, maxX = 0, maxY = 0;
        for (var p = 0; p < w * h; p++)
        {
            if (labels[p] < 0) continue;
            int x = p % w, y = p / w;
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x + 1);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y + 1);
        }
        return (minX, minY, maxX, maxY);
    }

    private static int NearestSimilarComponent(
        PngDecoder.Image img, int[] component, Dictionary<int, int> matched, int piece, int w, int h)
    {
        var pieceColor = MeanColor(img, component, piece);
        var depth = new Dictionary<int, int>();
        var queue = new Queue<int>();
        for (var p = 0; p < component.Length; p++)
            if (component[p] == piece) { depth[p] = 0; queue.Enqueue(p); }

        var candidates = new HashSet<int>();
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (depth[p] >= MergeSearchRadius) continue;
            foreach (var (nx, ny) in Neighbours4(p % w, p / w, w, h))
            {
                var n = ny * w + nx;
                if (depth.ContainsKey(n)) continue;
                depth[n] = depth[p] + 1;
                if (component[n] >= 0 && matched.ContainsKey(component[n])) candidates.Add(component[n]);
                else queue.Enqueue(n);
            }
        }

        return candidates
            .Select(c => (c, d: Distance(MeanColor(img, component, c), pieceColor)))
            .Where(t => t.d < 80)
            .OrderBy(t => t.d)
            .Select(t => t.c)
            .DefaultIfEmpty(-1)
            .First();
    }

    private static (byte R, byte G, byte B) MeanColor(PngDecoder.Image img, int[] component, int id)
    {
        long r = 0, g = 0, b = 0, n = 0;
        for (var p = 0; p < component.Length; p++)
        {
            if (component[p] != id) continue;
            var c = img.Get(p % img.Width, p / img.Width);
            r += c.R; g += c.G; b += c.B; n++;
        }
        return ((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }

    private static void Grow(int[] labels, int w, int h, int maxDepth, Func<int, bool> allowed)
    {
        var depth = new int[w * h];
        var queue = new Queue<int>();
        for (var p = 0; p < w * h; p++)
            if (labels[p] >= 0) queue.Enqueue(p);

        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (depth[p] >= maxDepth) continue;
            foreach (var (nx, ny) in Neighbours4(p % w, p / w, w, h))
            {
                var n = ny * w + nx;
                if (labels[n] >= 0 || !allowed(n)) continue;
                labels[n] = labels[p];
                depth[n] = depth[p] + 1;
                queue.Enqueue(n);
            }
        }
    }

    private static bool IsColored(PngDecoder.Image img, int x, int y)
    {
        var (r, g, b) = img.Get(x, y);
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        return max - min > 45 && max > 90;
    }

    private static int Distance((byte R, byte G, byte B) a, (byte R, byte G, byte B) b) =>
        Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    private static IEnumerable<(int X, int Y)> Neighbours4(int x, int y, int w, int h)
    {
        if (x > 0) yield return (x - 1, y);
        if (x < w - 1) yield return (x + 1, y);
        if (y > 0) yield return (x, y - 1);
        if (y < h - 1) yield return (x, y + 1);
    }
}
