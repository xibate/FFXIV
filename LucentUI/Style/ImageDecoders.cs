using System;
using System.Buffers.Binary;
using System.IO;

namespace LucentUI.Style;

/// <summary>A decoded image as 8-bit RGBA, top row first.</summary>
public sealed record RgbaImage(int Width, int Height, byte[] Pixels);

/// <summary>
/// Decoders for the image formats WoW addon media uses: TGA (uncompressed or RLE) and BLP2 (palette, DXT1/3/5 or
/// raw BGRA). PNG and JPG are left to Dalamud.
/// </summary>
public static class ImageDecoders
{
    private const int MaxSide = 4096;

    public static RgbaImage? Load(string path)
    {
        var data = File.ReadAllBytes(path);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".tga" => DecodeTga(data),
            ".blp" => DecodeBlp(data),
            _ => null,
        };
    }

    // ── TGA ──────────────────────────────────────────────────────────────────

    public static RgbaImage DecodeTga(byte[] d)
    {
        if (d.Length < 18)
        {
            throw new InvalidDataException("TGA file too short.");
        }

        var idLength = d[0];
        var colorMapType = d[1];
        var imageType = d[2];
        var mapFirst = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(3));
        var mapLength = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(5));
        var mapDepth = d[7];
        var width = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(12));
        var height = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(14));
        var depth = d[16];
        var descriptor = d[17];
        CheckSize(width, height);

        var pos = 18 + idLength;
        byte[][]? palette = null;
        if (colorMapType == 1)
        {
            var entryBytes = (mapDepth + 7) / 8;
            palette = new byte[mapFirst + mapLength][];
            for (var i = 0; i < mapLength; i++)
            {
                palette[mapFirst + i] = ReadTgaPixel(d, pos + i * entryBytes, mapDepth, false);
            }

            pos += mapLength * entryBytes;
        }

        var baseType = imageType & ~8;
        var rle = (imageType & 8) != 0;
        if (baseType is not (1 or 2 or 3))
        {
            throw new InvalidDataException($"TGA image type {imageType} is not supported.");
        }

        var gray = baseType == 3;
        var pixelBytes = (depth + 7) / 8;
        var pixels = new byte[width * height * 4];
        var count = width * height;

        byte[] Next(ref int p)
        {
            if (baseType == 1)
            {
                var index = pixelBytes == 1 ? d[p] : BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(p));
                p += pixelBytes;
                return palette != null && index < palette.Length && palette[index] != null ? palette[index] : new byte[] { 0, 0, 0, 255 };
            }

            var px = ReadTgaPixel(d, p, depth, gray);
            p += pixelBytes;
            return px;
        }

        var written = 0;
        void Put(byte[] px)
        {
            if (written < count)
            {
                Buffer.BlockCopy(px, 0, pixels, written * 4, 4);
                written++;
            }
        }

        while (written < count && pos < d.Length)
        {
            if (!rle)
            {
                Put(Next(ref pos));
                continue;
            }

            var header = d[pos++];
            var run = (header & 0x7F) + 1;
            if ((header & 0x80) != 0)
            {
                var px = Next(ref pos);
                for (var i = 0; i < run; i++)
                {
                    Put(px);
                }
            }
            else
            {
                for (var i = 0; i < run && pos < d.Length; i++)
                {
                    Put(Next(ref pos));
                }
            }
        }

        // An alpha channel of all zeros on a picture with colour almost always means "no alpha" (common in
        // exported TGAs). A fully black, fully clear image really is blank.
        var alphaBits = descriptor & 0x0F;
        if (depth == 32 && alphaBits == 0)
        {
            var anyAlpha = false;
            var anyColor = false;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                anyAlpha |= pixels[i + 3] != 0;
                anyColor |= pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0;
                if (anyAlpha)
                {
                    break;
                }
            }

            if (!anyAlpha && anyColor)
            {
                for (var i = 3; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255;
                }
            }
        }

        var rightToLeft = (descriptor & 0x10) != 0;
        var topToBottom = (descriptor & 0x20) != 0;
        if (!topToBottom)
        {
            FlipVertical(pixels, width, height);
        }

        if (rightToLeft)
        {
            FlipHorizontal(pixels, width, height);
        }

        return new RgbaImage(width, height, pixels);
    }

    private static byte[] ReadTgaPixel(byte[] d, int p, int depth, bool gray)
    {
        if (p + (depth + 7) / 8 > d.Length)
        {
            return new byte[] { 0, 0, 0, 0 };
        }

        if (gray)
        {
            var v = d[p];
            var a = depth == 16 ? d[p + 1] : (byte)255;
            return new[] { v, v, v, a };
        }

        switch (depth)
        {
            case 32:
                return new[] { d[p + 2], d[p + 1], d[p], d[p + 3] };
            case 24:
                return new[] { d[p + 2], d[p + 1], d[p], (byte)255 };
            case 15:
            case 16:
            {
                var v = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(p));
                var r = (byte)(((v >> 10) & 0x1F) * 255 / 31);
                var g = (byte)(((v >> 5) & 0x1F) * 255 / 31);
                var b = (byte)((v & 0x1F) * 255 / 31);
                var a = depth == 16 && (v & 0x8000) == 0 ? (byte)0 : (byte)255;
                return new[] { r, g, b, depth == 15 ? (byte)255 : a };
            }

            case 8:
                return new[] { d[p], d[p], d[p], (byte)255 };
            default:
                throw new InvalidDataException($"TGA pixel depth {depth} is not supported.");
        }
    }

    // ── BLP2 ─────────────────────────────────────────────────────────────────

    public static RgbaImage DecodeBlp(byte[] d)
    {
        if (d.Length < 148 || d[0] != 'B' || d[1] != 'L' || d[2] != 'P' || d[3] != '2')
        {
            throw new InvalidDataException("Only BLP2 files are supported.");
        }

        var compression = d[8];
        var alphaDepth = d[9];
        var alphaType = d[10];
        var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(12));
        var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(16));
        CheckSize(width, height);
        var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(20));
        var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(84));
        if (offset <= 0 || offset + size > d.Length)
        {
            throw new InvalidDataException("BLP mipmap is out of range.");
        }

        var src = d.AsSpan(offset, size);
        var pixels = new byte[width * height * 4];
        switch (compression)
        {
            case 1:
                DecodeBlpPalette(d, src, alphaDepth, width, height, pixels);
                break;
            case 2:
                DecodeDxt(src, width, height, pixels, alphaDepth <= 1 ? 1 : alphaType == 7 ? 5 : 3, alphaDepth > 0);
                break;
            case 3:
                for (var i = 0; i < width * height && i * 4 + 3 < src.Length; i++)
                {
                    pixels[i * 4] = src[i * 4 + 2];
                    pixels[i * 4 + 1] = src[i * 4 + 1];
                    pixels[i * 4 + 2] = src[i * 4];
                    pixels[i * 4 + 3] = src[i * 4 + 3];
                }

                break;
            default:
                throw new InvalidDataException($"BLP compression {compression} is not supported.");
        }

        return new RgbaImage(width, height, pixels);
    }

    private static void DecodeBlpPalette(byte[] file, ReadOnlySpan<byte> src, int alphaDepth, int width, int height, byte[] pixels)
    {
        var count = width * height;
        for (var i = 0; i < count && i < src.Length; i++)
        {
            var entry = 148 + src[i] * 4;
            pixels[i * 4] = file[entry + 2];
            pixels[i * 4 + 1] = file[entry + 1];
            pixels[i * 4 + 2] = file[entry];

            var alpha = (byte)255;
            switch (alphaDepth)
            {
                case 1 when count + i / 8 < src.Length:
                    alpha = (src[count + i / 8] >> (i % 8) & 1) != 0 ? (byte)255 : (byte)0;
                    break;
                case 4 when count + i / 2 < src.Length:
                    alpha = (byte)(((src[count + i / 2] >> (i % 2 * 4)) & 0x0F) * 17);
                    break;
                case 8 when count + i < src.Length:
                    alpha = src[count + i];
                    break;
            }

            pixels[i * 4 + 3] = alpha;
        }
    }

    /// <summary>Decodes DXT1 (format 1), DXT3 (3) or DXT5 (5) blocks into RGBA.</summary>
    public static void DecodeDxt(ReadOnlySpan<byte> src, int width, int height, byte[] pixels, int format, bool dxt1Alpha = true)
    {
        var blockBytes = format == 1 ? 8 : 16;
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        Span<byte> colors = stackalloc byte[16];
        Span<byte> alphas = stackalloc byte[16];

        for (var by = 0; by < blocksY; by++)
        {
            for (var bx = 0; bx < blocksX; bx++)
            {
                var b = (by * blocksX + bx) * blockBytes;
                if (b + blockBytes > src.Length)
                {
                    return;
                }

                var block = src.Slice(b, blockBytes);
                alphas.Fill(255);
                if (format == 3)
                {
                    for (var i = 0; i < 16; i++)
                    {
                        alphas[i] = (byte)(((block[i / 2] >> (i % 2 * 4)) & 0x0F) * 17);
                    }
                }
                else if (format == 5)
                {
                    DecodeDxt5Alpha(block[..8], alphas);
                }

                var colorBlock = format == 1 ? block : block[8..];
                var c0 = BinaryPrimitives.ReadUInt16LittleEndian(colorBlock);
                var c1 = BinaryPrimitives.ReadUInt16LittleEndian(colorBlock[2..]);
                Rgb565(c0, colors[..4]);
                Rgb565(c1, colors.Slice(4, 4));
                var fourColors = format != 1 || c0 > c1;
                for (var ch = 0; ch < 3; ch++)
                {
                    if (fourColors)
                    {
                        colors[8 + ch] = (byte)((2 * colors[ch] + colors[4 + ch]) / 3);
                        colors[12 + ch] = (byte)((colors[ch] + 2 * colors[4 + ch]) / 3);
                    }
                    else
                    {
                        colors[8 + ch] = (byte)((colors[ch] + colors[4 + ch]) / 2);
                        colors[12 + ch] = 0;
                    }
                }

                colors[3] = colors[7] = colors[11] = 255;
                colors[15] = fourColors || !dxt1Alpha ? (byte)255 : (byte)0;

                var indices = BinaryPrimitives.ReadUInt32LittleEndian(colorBlock[4..]);
                for (var i = 0; i < 16; i++)
                {
                    var x = bx * 4 + i % 4;
                    var y = by * 4 + i / 4;
                    if (x >= width || y >= height)
                    {
                        continue;
                    }

                    var index = (int)((indices >> (2 * i)) & 3);
                    var o = (y * width + x) * 4;
                    pixels[o] = colors[index * 4];
                    pixels[o + 1] = colors[index * 4 + 1];
                    pixels[o + 2] = colors[index * 4 + 2];
                    pixels[o + 3] = format == 1 ? colors[index * 4 + 3] : alphas[i];
                }
            }
        }
    }

    private static void DecodeDxt5Alpha(ReadOnlySpan<byte> block, Span<byte> alphas)
    {
        Span<byte> table = stackalloc byte[8];
        table[0] = block[0];
        table[1] = block[1];
        if (table[0] > table[1])
        {
            for (var i = 1; i < 7; i++)
            {
                table[i + 1] = (byte)(((7 - i) * table[0] + i * table[1]) / 7);
            }
        }
        else
        {
            for (var i = 1; i < 5; i++)
            {
                table[i + 1] = (byte)(((5 - i) * table[0] + i * table[1]) / 5);
            }

            table[6] = 0;
            table[7] = 255;
        }

        ulong bits = 0;
        for (var i = 0; i < 6; i++)
        {
            bits |= (ulong)block[2 + i] << (8 * i);
        }

        for (var i = 0; i < 16; i++)
        {
            alphas[i] = table[(int)((bits >> (3 * i)) & 7)];
        }
    }

    private static void Rgb565(ushort c, Span<byte> rgba)
    {
        rgba[0] = (byte)(((c >> 11) & 0x1F) * 255 / 31);
        rgba[1] = (byte)(((c >> 5) & 0x3F) * 255 / 63);
        rgba[2] = (byte)((c & 0x1F) * 255 / 31);
        rgba[3] = 255;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void CheckSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > MaxSide || height > MaxSide)
        {
            throw new InvalidDataException($"Image size {width}x{height} is not supported.");
        }
    }

    private static void FlipVertical(byte[] pixels, int width, int height)
    {
        var stride = width * 4;
        var row = new byte[stride];
        for (var y = 0; y < height / 2; y++)
        {
            var top = y * stride;
            var bottom = (height - 1 - y) * stride;
            Buffer.BlockCopy(pixels, top, row, 0, stride);
            Buffer.BlockCopy(pixels, bottom, pixels, top, stride);
            Buffer.BlockCopy(row, 0, pixels, bottom, stride);
        }
    }

    private static void FlipHorizontal(byte[] pixels, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width / 2; x++)
            {
                var a = (y * width + x) * 4;
                var b = (y * width + width - 1 - x) * 4;
                for (var c = 0; c < 4; c++)
                {
                    (pixels[a + c], pixels[b + c]) = (pixels[b + c], pixels[a + c]);
                }
            }
        }
    }
}
