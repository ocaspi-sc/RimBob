using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace RimBob.Host;

internal static class PngThumbnailer
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly byte[] Ihdr = Encoding.ASCII.GetBytes("IHDR");
    private static readonly byte[] Idat = Encoding.ASCII.GetBytes("IDAT");
    private static readonly byte[] Iend = Encoding.ASCII.GetBytes("IEND");
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] DownscaleToFit(byte[] png, int maxDimension)
    {
        if (maxDimension < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDimension), "Max dimension must be positive.");
        }

        DecodedPng? decoded = TryDecodeRgba(png);
        if (decoded is null || decoded.Width <= maxDimension && decoded.Height <= maxDimension)
        {
            return png;
        }

        double scale = Math.Min((double)maxDimension / decoded.Width, (double)maxDimension / decoded.Height);
        int width = Math.Max(1, (int)Math.Round(decoded.Width * scale));
        int height = Math.Max(1, (int)Math.Round(decoded.Height * scale));
        byte[] resized = ResizeNearest(decoded.Rgba, decoded.Width, decoded.Height, width, height);
        return EncodeRgba(width, height, resized);
    }

    private static DecodedPng? TryDecodeRgba(byte[] png)
    {
        if (png.Length < Signature.Length || !png.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            throw new FormatException("PNG signature was invalid.");
        }

        int offset = Signature.Length;
        int width = 0;
        int height = 0;
        int bitDepth = 0;
        int colorType = 0;
        int compressionMethod = 0;
        int filterMethod = 0;
        int interlaceMethod = 0;
        List<byte[]> idatChunks = [];

        while (offset + 8 <= png.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4)));
            ReadOnlySpan<byte> type = png.AsSpan(offset + 4, 4);
            int dataOffset = offset + 8;
            int crcOffset = checked(dataOffset + length);
            if (length < 0 || crcOffset + 4 > png.Length)
            {
                throw new FormatException("PNG chunk length was invalid.");
            }

            ReadOnlySpan<byte> data = png.AsSpan(dataOffset, length);
            if (type.SequenceEqual(Ihdr))
            {
                if (length != 13)
                {
                    throw new FormatException("PNG IHDR chunk length was invalid.");
                }

                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[..4]));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4)));
                bitDepth = data[8];
                colorType = data[9];
                compressionMethod = data[10];
                filterMethod = data[11];
                interlaceMethod = data[12];
            }
            else if (type.SequenceEqual(Idat))
            {
                idatChunks.Add(data.ToArray());
            }
            else if (type.SequenceEqual(Iend))
            {
                break;
            }

            offset = crcOffset + 4;
        }

        if (width <= 0 || height <= 0 || idatChunks.Count == 0)
        {
            throw new FormatException("PNG was missing required image data.");
        }

        if (bitDepth != 8 || colorType is not 2 and not 6 || compressionMethod != 0 || filterMethod != 0 || interlaceMethod != 0)
        {
            return null;
        }

        int sourceBytesPerPixel = colorType == 6 ? 4 : 3;
        int sourceStride = checked(width * sourceBytesPerPixel);
        int expectedInflatedLength = checked((sourceStride + 1) * height);
        byte[] inflated = Inflate(idatChunks, expectedInflatedLength);
        if (inflated.Length < expectedInflatedLength)
        {
            throw new FormatException("PNG image data was shorter than expected.");
        }

        byte[] rgba = new byte[checked(width * height * 4)];
        byte[] previous = new byte[sourceStride];
        byte[] current = new byte[sourceStride];
        int readOffset = 0;

        for (int y = 0; y < height; y++)
        {
            int filter = inflated[readOffset++];
            UnfilterScanline(filter, inflated, ref readOffset, current, previous, sourceBytesPerPixel);
            CopyToRgba(current, rgba, y, width, sourceBytesPerPixel);
            (previous, current) = (current, previous);
        }

        return new DecodedPng(width, height, rgba);
    }

    private static byte[] Inflate(IReadOnlyList<byte[]> chunks, int capacity)
    {
        using MemoryStream compressed = new();
        foreach (byte[] chunk in chunks)
        {
            compressed.Write(chunk);
        }

        compressed.Position = 0;
        using ZLibStream zlib = new(compressed, CompressionMode.Decompress);
        using MemoryStream raw = new(capacity);
        zlib.CopyTo(raw);
        return raw.ToArray();
    }

    private static void UnfilterScanline(
        int filter,
        byte[] source,
        ref int readOffset,
        byte[] current,
        byte[] previous,
        int bytesPerPixel)
    {
        for (int i = 0; i < current.Length; i++)
        {
            int value = source[readOffset++];
            int left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
            int up = previous[i];
            int upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;

            int predictor = filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upLeft),
                _ => throw new FormatException($"Unsupported PNG scanline filter {filter}.")
            };
            current[i] = unchecked((byte)(value + predictor));
        }
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        int p = left + up - upLeft;
        int pa = Math.Abs(p - left);
        int pb = Math.Abs(p - up);
        int pc = Math.Abs(p - upLeft);
        if (pa <= pb && pa <= pc) return left;
        return pb <= pc ? up : upLeft;
    }

    private static void CopyToRgba(byte[] source, byte[] rgba, int y, int width, int sourceBytesPerPixel)
    {
        int outputRow = checked(y * width * 4);
        for (int x = 0; x < width; x++)
        {
            int sourceIndex = x * sourceBytesPerPixel;
            int outputIndex = outputRow + x * 4;
            rgba[outputIndex] = source[sourceIndex];
            rgba[outputIndex + 1] = source[sourceIndex + 1];
            rgba[outputIndex + 2] = source[sourceIndex + 2];
            rgba[outputIndex + 3] = sourceBytesPerPixel == 4 ? source[sourceIndex + 3] : (byte)255;
        }
    }

    private static byte[] ResizeNearest(byte[] source, int sourceWidth, int sourceHeight, int width, int height)
    {
        byte[] resized = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        {
            int sourceY = Math.Min(sourceHeight - 1, y * sourceHeight / height);
            for (int x = 0; x < width; x++)
            {
                int sourceX = Math.Min(sourceWidth - 1, x * sourceWidth / width);
                int sourceIndex = checked((sourceY * sourceWidth + sourceX) * 4);
                int outputIndex = checked((y * width + x) * 4);
                resized[outputIndex] = source[sourceIndex];
                resized[outputIndex + 1] = source[sourceIndex + 1];
                resized[outputIndex + 2] = source[sourceIndex + 2];
                resized[outputIndex + 3] = source[sourceIndex + 3];
            }
        }

        return resized;
    }

    private static byte[] EncodeRgba(int width, int height, byte[] rgba)
    {
        using MemoryStream raw = new();
        int stride = checked(width * 4);
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgba, y * stride, stride);
        }

        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        using MemoryStream png = new();
        png.Write(Signature);
        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), checked((uint)height));
        ihdr[8] = 8;
        ihdr[9] = 6;
        WriteChunk(png, Ihdr, ihdr);
        WriteChunk(png, Idat, compressed.ToArray());
        WriteChunk(png, Iend, []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream output, byte[] type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        output.Write(length);
        output.Write(type);
        output.Write(data);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(type, data));
        output.Write(crc);
    }

    private static uint Crc(byte[] type, byte[] data)
    {
        uint crc = 0xffffffffu;
        foreach (byte value in type)
        {
            crc = CrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
        }

        foreach (byte value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
        }

        return crc ^ 0xffffffffu;
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint crc = i;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 1 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
            }

            table[i] = crc;
        }

        return table;
    }

    private sealed record DecodedPng(int Width, int Height, byte[] Rgba);
}
