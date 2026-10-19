using System.IO.Compression;

namespace MapBuilder;

/// <summary>
/// Minimal PNG decoder (8-bit RGB/RGBA, non-interlaced) so the tool needs no NuGet packages.
/// </summary>
public static class PngDecoder
{
    public sealed record Image(int Width, int Height, byte[] Rgba)
    {
        public (byte R, byte G, byte B) Get(int x, int y)
        {
            var i = (y * Width + x) * 4;
            return (Rgba[i], Rgba[i + 1], Rgba[i + 2]);
        }
    }

    public static Image Decode(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);

        var signature = br.ReadBytes(8);
        if (signature[0] != 0x89 || signature[1] != 'P' || signature[2] != 'N' || signature[3] != 'G')
            throw new InvalidDataException("Not a PNG file.");

        int width = 0, height = 0, colorType = 0;
        using var idat = new MemoryStream();

        while (fs.Position < fs.Length)
        {
            var length = ReadInt32BigEndian(br);
            var type = new string(br.ReadChars(4));
            var data = br.ReadBytes(length);
            br.ReadBytes(4); // CRC

            switch (type)
            {
                case "IHDR":
                    width = (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
                    height = (data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7];
                    var bitDepth = data[8];
                    colorType = data[9];
                    var interlace = data[12];
                    if (bitDepth != 8 || (colorType != 2 && colorType != 6) || interlace != 0)
                        throw new NotSupportedException("Only 8-bit, non-interlaced RGB/RGBA PNGs are supported.");
                    break;
                case "IDAT":
                    idat.Write(data);
                    break;
                case "IEND":
                    fs.Position = fs.Length;
                    break;
            }
        }

        var bpp = colorType == 6 ? 4 : 3;
        var stride = width * bpp;
        var raw = new byte[height * stride];

        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        {
            var prev = new byte[stride];
            var line = new byte[stride];
            for (var y = 0; y < height; y++)
            {
                var filter = z.ReadByte();
                z.ReadExactly(line);
                Unfilter(filter, line, prev, bpp);
                Buffer.BlockCopy(line, 0, raw, y * stride, stride);
                (prev, line) = (line, prev);
            }
        }

        var rgba = new byte[width * height * 4];
        for (int p = 0, s = 0; p < width * height; p++, s += bpp)
        {
            rgba[p * 4] = raw[s];
            rgba[p * 4 + 1] = raw[s + 1];
            rgba[p * 4 + 2] = raw[s + 2];
            rgba[p * 4 + 3] = bpp == 4 ? raw[s + 3] : (byte)255;
        }

        return new Image(width, height, rgba);
    }

    private static void Unfilter(int filter, byte[] line, byte[] prev, int bpp)
    {
        for (var i = 0; i < line.Length; i++)
        {
            int a = i >= bpp ? line[i - bpp] : 0;
            int b = prev[i];
            int c = i >= bpp ? prev[i - bpp] : 0;
            line[i] = filter switch
            {
                0 => line[i],
                1 => (byte)(line[i] + a),
                2 => (byte)(line[i] + b),
                3 => (byte)(line[i] + ((a + b) >> 1)),
                4 => (byte)(line[i] + Paeth(a, b, c)),
                _ => throw new InvalidDataException($"Unknown PNG filter {filter}."),
            };
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }

    private static int ReadInt32BigEndian(BinaryReader br)
    {
        var b = br.ReadBytes(4);
        return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
    }
}
