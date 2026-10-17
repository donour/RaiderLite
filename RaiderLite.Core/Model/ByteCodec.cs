using System.Buffers.Binary;

namespace RaiderLite.Core.Model;

/// <summary>
/// Reads and writes raw values in an image. Port of RomRaider's RomAttributeParser byte routines.
/// </summary>
public static class ByteCodec
{
    public static long ReadInteger(byte[] input, Endian endian, int address, int storageType, bool signed)
    {
        bool little = endian == Endian.Little;
        switch (storageType)
        {
            case 1:
                CheckRange(input, address, 1);
                return signed ? (sbyte)input[address] : input[address];
            case 2:
            {
                CheckRange(input, address, 2);
                var span = input.AsSpan(address, 2);
                return signed
                    ? (little ? BinaryPrimitives.ReadInt16LittleEndian(span) : BinaryPrimitives.ReadInt16BigEndian(span))
                    : (little ? BinaryPrimitives.ReadUInt16LittleEndian(span) : BinaryPrimitives.ReadUInt16BigEndian(span));
            }
            case 4:
            {
                CheckRange(input, address, 4);
                var span = input.AsSpan(address, 4);
                return signed
                    ? (little ? BinaryPrimitives.ReadInt32LittleEndian(span) : BinaryPrimitives.ReadInt32BigEndian(span))
                    : (little ? BinaryPrimitives.ReadUInt32LittleEndian(span) : BinaryPrimitives.ReadUInt32BigEndian(span));
            }
            case StorageType.Movi20:
            case StorageType.Movi20S:
            {
                // The 20-bit immediate sits in the 3 bytes at address; RomRaider reads the
                // 32-bit word that ends at address+2 and decodes it.
                CheckRange(input, address - 1, 4);
                var span = input.AsSpan(address - 1, 4);
                int word = little ? BinaryPrimitives.ReadInt32LittleEndian(span) : BinaryPrimitives.ReadInt32BigEndian(span);
                return DecodeMovi20(word);
            }
            default:
                throw new NotSupportedException($"Unsupported storage type {storageType}");
        }
    }

    /// <summary>Reads a value and extracts the bits under <paramref name="mask"/>, shifted down.
    /// Assumes the mask bits are contiguous.</summary>
    public static long ReadMasked(byte[] input, Endian endian, int address, int storageType, bool signed, int mask)
    {
        long value = ReadInteger(input, endian, address, storageType, signed) & (uint)mask;
        int shift = MaskUtil.FirstOneOfMask(mask);
        return shift < 0 ? 0 : value >> shift;
    }

    private static int DecodeMovi20(int value)
    {
        int shift = value & 0x00010000;
        value = (int)(((uint)(value & 0x00f00000) >> 4) + (value & 0x0000FFFF));
        if ((value & 0x00080000) > 0)
            value |= unchecked((int)0xfff00000);
        if (shift > 0) // MOVI20S
            return value << 8;
        return value;
    }

    /// <summary>Encodes an integer to the byte layout of <paramref name="storageType"/>.</summary>
    public static byte[] EncodeInteger(int input, Endian endian, int storageType)
    {
        bool little = endian == Endian.Little;
        switch (storageType)
        {
            case 1:
                return [(byte)input];
            case 2:
            {
                var b = new byte[2];
                if (little) BinaryPrimitives.WriteInt16LittleEndian(b, (short)input);
                else BinaryPrimitives.WriteInt16BigEndian(b, (short)input);
                return b;
            }
            case 4:
            {
                var b = new byte[4];
                if (little) BinaryPrimitives.WriteInt32LittleEndian(b, input);
                else BinaryPrimitives.WriteInt32BigEndian(b, input);
                return b;
            }
            case StorageType.Movi20:
            case StorageType.Movi20S:
            {
                var b = new byte[4];
                int v = storageType == StorageType.Movi20S ? input >> 8 : input;
                if (little) BinaryPrimitives.WriteInt32LittleEndian(b, v);
                else BinaryPrimitives.WriteInt32BigEndian(b, v);
                byte first = (byte)(b[1] << 4);
                if (storageType == StorageType.Movi20S)
                    first |= 0x01;
                return [first, b[2], b[3]];
            }
            default:
                throw new NotSupportedException($"Unsupported storage type {storageType}");
        }
    }

    /// <summary>
    /// Float byte order. RomRaider ignores the table's own endian for floats unless the
    /// memory model specifies one: legacy definitions marked big-endian floats as "little".
    /// </summary>
    private static bool FloatIsLittle(Endian? memModelEndian) => memModelEndian == Endian.Little;

    public static float ReadFloat(byte[] input, int address, Endian? memModelEndian)
    {
        CheckRange(input, address, 4);
        var span = input.AsSpan(address, 4);
        return FloatIsLittle(memModelEndian)
            ? BinaryPrimitives.ReadSingleLittleEndian(span)
            : BinaryPrimitives.ReadSingleBigEndian(span);
    }

    public static byte[] EncodeFloat(float value, Endian? memModelEndian)
    {
        var b = new byte[4];
        if (FloatIsLittle(memModelEndian))
            BinaryPrimitives.WriteSingleLittleEndian(b, value);
        else
            BinaryPrimitives.WriteSingleBigEndian(b, value);
        return b;
    }

    /// <summary>Parses RomRaider file sizes: "15540b", "2kb", "1mb", or a plain number.</summary>
    public static int ParseFileSize(string input)
    {
        input = input.Trim();
        if (input.EndsWith("kb", StringComparison.OrdinalIgnoreCase))
            return int.Parse(input[..^2]) * 1024;
        if (input.EndsWith("mb", StringComparison.OrdinalIgnoreCase))
            return int.Parse(input[..^2]) * 1024 * 1024;
        if (input.EndsWith('b') || input.EndsWith('B'))
            return int.Parse(input[..^1]);
        return int.Parse(input);
    }

    private static void CheckRange(byte[] input, int address, int length)
    {
        if (address < 0 || address + length > input.Length)
            throw new IndexOutOfRangeException(
                $"Address 0x{address:X} (+{length}) is outside the image (size 0x{input.Length:X})");
    }
}
