// MapBuilder: turns the reference PNG (docs/reference/bulgaria-provinces.png) into
//   1) wwwroot/data/map-geometry.json  – SVG paths + label anchors (client rendering)
//   2) Data/provinces.json             – names, ISO codes, neighbours (server game data), only with --provinces
//
// Usage (from repo root):
//   dotnet run --project tools/MapBuilder -- [--provinces]

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using MapBuilder;

var root = FindRepoRoot();
var imagePath = Path.Combine(root, "docs", "reference", "bulgaria-provinces.png");
var geometryOut = Path.Combine(root, "src", "BulgariaConquest.Server", "wwwroot", "data", "map-geometry.json");
var provincesOut = Path.Combine(root, "src", "BulgariaConquest.Server", "Data", "provinces.json");
var writeProvinces = args.Contains("--provinces");

var image = PngDecoder.Decode(imagePath);
Console.WriteLine($"Image {image.Width}x{image.Height}");

var labels = Segmentation.LabelRegions(image, ProvinceCatalog.All, out var labelToProvince);
var arcs = Topology.BuildArcs(labels, image.Width, image.Height);
Console.WriteLine($"Arcs: {arcs.Count}");

foreach (var arc in arcs)
    arc.Points = Geometry.Smooth(Geometry.Simplify(arc.Points, 1.3), 2);

var anchors = Segmentation.LabelAnchors(labels, image.Width, image.Height, labelToProvince.Length);
var (minX, minY, maxX, maxY) = Segmentation.Bounds(labels, image.Width, image.Height);
const int margin = 8;

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
};

var geometry = new
{
    source = "docs/reference/bulgaria-provinces.png",
    viewBox = new[] { minX - margin, minY - margin, maxX - minX + 2 * margin, maxY - minY + 2 * margin },
    provinces = labelToProvince.Select((p, label) => new
    {
        id = p.Id,
        path = Geometry.ToSvgPath(Topology.AssembleRings(arcs, label)),
        labelX = anchors[label].X,
        labelY = anchors[label].Y,
    }).OrderBy(p => p.id).ToArray(),
};

Directory.CreateDirectory(Path.GetDirectoryName(geometryOut)!);
File.WriteAllText(geometryOut, JsonSerializer.Serialize(geometry, jsonOptions));
Console.WriteLine($"Wrote {geometryOut}");

// Neighbours: provinces that share a border of at least a few pixels (corner touches are ignored).
var borderLength = new Dictionary<(int, int), double>();
foreach (var arc in arcs.Where(a => a.Left >= 0 && a.Right >= 0))
{
    var key = (Math.Min(arc.Left, arc.Right), Math.Max(arc.Left, arc.Right));
    borderLength[key] = borderLength.GetValueOrDefault(key) + arc.RawLength;
}

var neighbours = labelToProvince.Select(_ => new SortedSet<string>()).ToArray();
foreach (var ((a, b), length) in borderLength.Where(kv => kv.Value >= 6))
{
    neighbours[a].Add(labelToProvince[b].Id);
    neighbours[b].Add(labelToProvince[a].Id);
}

for (var i = 0; i < labelToProvince.Length; i++)
    Console.WriteLine($"{labelToProvince[i].Name,-16} -> {string.Join(", ", neighbours[i])}");

if (writeProvinces)
{
    var provinces = labelToProvince
        .Select((p, i) => new { id = p.Id, iso = p.Iso, name = p.Name, neighbors = neighbours[i].ToArray() })
        .OrderBy(p => p.iso)
        .ToArray();
    Directory.CreateDirectory(Path.GetDirectoryName(provincesOut)!);
    File.WriteAllText(provincesOut, JsonSerializer.Serialize(new { provinces }, jsonOptions));
    Console.WriteLine($"Wrote {provincesOut}");
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BulgariaConquest.sln")))
        dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Run from inside the repository.");
}
