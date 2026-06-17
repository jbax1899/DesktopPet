using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopPet.App.Overlay;

public static class InochiPuppetLoader
{
    private static readonly byte[] InpHeader = "TRNSRTS\0"u8.ToArray();
    private static readonly byte[] TextureSectionHeader = "TEX_SECT"u8.ToArray();

    public static InochiPuppet Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (!bytes.AsSpan(0, InpHeader.Length).SequenceEqual(InpHeader))
        {
            throw new InvalidOperationException("The puppet file does not use the expected INP container header.");
        }

        var puppetJsonLength = ReadBigEndianInt32(bytes, InpHeader.Length);
        var puppetJsonStart = InpHeader.Length + sizeof(int);
        using var document = JsonDocument.Parse(bytes.AsMemory(puppetJsonStart, puppetJsonLength));

        var textureOffset = puppetJsonStart + puppetJsonLength;
        var atlas = ReadTextureAtlas(bytes, textureOffset);
        var parts = ReadParts(document.RootElement.GetProperty("nodes"), atlas.PixelWidth, atlas.PixelHeight);
        var bounds = GetPuppetBounds(parts);

        // The INP coordinates are centered around the puppet; WPF Canvas coordinates start at top-left.
        var normalizedParts = parts
            .Select(part => part with
            {
                Bounds = new Rect(
                    part.Bounds.X - bounds.X,
                    part.Bounds.Y - bounds.Y,
                    part.Bounds.Width,
                    part.Bounds.Height)
            })
            // In this export, larger zsort values are farther back. WPF draws later children on top,
            // so add back layers first and foreground details last.
            .OrderByDescending(part => part.ZSort)
            .ToArray();

        return new InochiPuppet(atlas, bounds.Width, bounds.Height, normalizedParts);
    }

    private static BitmapSource ReadTextureAtlas(byte[] bytes, int textureOffset)
    {
        if (!bytes.AsSpan(textureOffset, TextureSectionHeader.Length).SequenceEqual(TextureSectionHeader))
        {
            throw new InvalidOperationException("The puppet file does not contain the expected TEX_SECT section.");
        }

        var textureCount = ReadBigEndianInt32(bytes, textureOffset + TextureSectionHeader.Length);
        if (textureCount != 1)
        {
            throw new NotSupportedException($"This prototype supports one texture atlas, but the puppet has {textureCount}.");
        }

        var payloadLengthOffset = textureOffset + TextureSectionHeader.Length + sizeof(int);
        var payloadLength = ReadBigEndianInt32(bytes, payloadLengthOffset);
        var textureFormat = bytes[payloadLengthOffset + sizeof(int)];
        var payloadOffset = payloadLengthOffset + sizeof(int) + 1;

        if (textureFormat != 1)
        {
            throw new NotSupportedException($"This prototype supports TGA texture encoding 1, but the puppet uses {textureFormat}.");
        }

        return DecodeTga(bytes.AsSpan(payloadOffset, payloadLength));
    }

    private static IReadOnlyList<InochiPart> ReadParts(JsonElement rootNode, int atlasWidth, int atlasHeight)
    {
        var parts = new List<InochiPart>();
        WalkNode(rootNode, parentX: 0, parentY: 0, parts, atlasWidth, atlasHeight);
        return parts;
    }

    private static void WalkNode(
        JsonElement node,
        double parentX,
        double parentY,
        List<InochiPart> parts,
        int atlasWidth,
        int atlasHeight)
    {
        var transform = node.GetProperty("transform").GetProperty("trans");
        var nodeX = parentX + transform[0].GetDouble();
        var nodeY = parentY + transform[1].GetDouble();

        if (node.GetProperty("type").GetString() == "Part")
        {
            parts.Add(ReadPart(node, nodeX, nodeY, atlasWidth, atlasHeight));
        }

        if (!node.TryGetProperty("children", out var children))
        {
            return;
        }

        foreach (var child in children.EnumerateArray())
        {
            WalkNode(child, nodeX, nodeY, parts, atlasWidth, atlasHeight);
        }
    }

    private static InochiPart ReadPart(JsonElement node, double nodeX, double nodeY, int atlasWidth, int atlasHeight)
    {
        var mesh = node.GetProperty("mesh");
        var verts = mesh.GetProperty("verts").EnumerateArray().Select(value => value.GetDouble()).ToArray();
        var uvs = mesh.GetProperty("uvs").EnumerateArray().Select(value => value.GetDouble()).ToArray();

        var minX = verts.Where((_, index) => index % 2 == 0).Min() + nodeX;
        var maxX = verts.Where((_, index) => index % 2 == 0).Max() + nodeX;
        var minY = verts.Where((_, index) => index % 2 == 1).Min() + nodeY;
        var maxY = verts.Where((_, index) => index % 2 == 1).Max() + nodeY;

        var minU = uvs.Where((_, index) => index % 2 == 0).Min();
        var maxU = uvs.Where((_, index) => index % 2 == 0).Max();
        var minV = uvs.Where((_, index) => index % 2 == 1).Min();
        var maxV = uvs.Where((_, index) => index % 2 == 1).Max();

        return new InochiPart(
            node.GetProperty("name").GetString() ?? string.Empty,
            new Int32Rect(
                (int)Math.Round(minU * atlasWidth),
                (int)Math.Round(minV * atlasHeight),
                Math.Max(1, (int)Math.Round((maxU - minU) * atlasWidth)),
                Math.Max(1, (int)Math.Round((maxV - minV) * atlasHeight))),
            new Rect(minX, minY, maxX - minX, maxY - minY),
            node.GetProperty("zsort").GetDouble());
    }

    private static Rect GetPuppetBounds(IReadOnlyList<InochiPart> parts)
    {
        var minX = parts.Min(part => part.Bounds.X);
        var minY = parts.Min(part => part.Bounds.Y);
        var maxX = parts.Max(part => part.Bounds.Right);
        var maxY = parts.Max(part => part.Bounds.Bottom);

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static BitmapSource DecodeTga(ReadOnlySpan<byte> data)
    {
        var idLength = data[0];
        var imageType = data[2];
        var width = ReadLittleEndianInt16(data, 12);
        var height = ReadLittleEndianInt16(data, 14);
        var bitsPerPixel = data[16];
        var descriptor = data[17];

        if (imageType != 10 || bitsPerPixel != 32)
        {
            throw new NotSupportedException($"This prototype supports RLE-compressed 32-bit TGA atlases. Found type {imageType}, {bitsPerPixel} bpp.");
        }

        var pixels = new byte[width * height * 4];
        var sourceOffset = 18 + idLength;
        var sourceRow = 0;
        var sourceColumn = 0;
        var topOrigin = (descriptor & 0x20) != 0;
        var rightOrigin = (descriptor & 0x10) != 0;

        while (sourceRow < height)
        {
            var packet = data[sourceOffset++];
            var count = (packet & 0x7f) + 1;
            var isRunLengthPacket = (packet & 0x80) != 0;

            if (isRunLengthPacket)
            {
                var blue = data[sourceOffset++];
                var green = data[sourceOffset++];
                var red = data[sourceOffset++];
                var alpha = data[sourceOffset++];

                for (var i = 0; i < count; i++)
                {
                    WritePixel(pixels, width, height, sourceRow, sourceColumn, blue, green, red, alpha, topOrigin, rightOrigin);
                    AdvancePixel(width, ref sourceRow, ref sourceColumn);
                }
            }
            else
            {
                for (var i = 0; i < count; i++)
                {
                    var blue = data[sourceOffset++];
                    var green = data[sourceOffset++];
                    var red = data[sourceOffset++];
                    var alpha = data[sourceOffset++];
                    WritePixel(pixels, width, height, sourceRow, sourceColumn, blue, green, red, alpha, topOrigin, rightOrigin);
                    AdvancePixel(width, ref sourceRow, ref sourceColumn);
                }
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static void WritePixel(
        byte[] pixels,
        int width,
        int height,
        int sourceRow,
        int sourceColumn,
        byte blue,
        byte green,
        byte red,
        byte alpha,
        bool topOrigin,
        bool rightOrigin)
    {
        var targetRow = topOrigin ? sourceRow : height - sourceRow - 1;
        var targetColumn = rightOrigin ? width - sourceColumn - 1 : sourceColumn;
        var targetOffset = (targetRow * width + targetColumn) * 4;

        pixels[targetOffset] = blue;
        pixels[targetOffset + 1] = green;
        pixels[targetOffset + 2] = red;
        pixels[targetOffset + 3] = alpha;
    }

    private static void AdvancePixel(int width, ref int row, ref int column)
    {
        column++;
        if (column < width)
        {
            return;
        }

        column = 0;
        row++;
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset)
    {
        return (bytes[offset] << 24)
            | (bytes[offset + 1] << 16)
            | (bytes[offset + 2] << 8)
            | bytes[offset + 3];
    }

    private static int ReadLittleEndianInt16(ReadOnlySpan<byte> bytes, int offset)
    {
        return bytes[offset] | (bytes[offset + 1] << 8);
    }
}
