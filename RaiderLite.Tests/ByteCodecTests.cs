using System.Globalization;
using RaiderLite.Core.Model;

namespace RaiderLite.Tests;

public class ByteCodecTests
{
    private static readonly byte[] Data = [0x12, 0x34, 0x56, 0x78, 0xFF, 0xFE, 0x80, 0x00];

    [Fact]
    public void ReadsIntegers()
    {
        Assert.Equal(0x12, ByteCodec.ReadInteger(Data, Endian.Big, 0, 1, false));
        Assert.Equal(0x1234, ByteCodec.ReadInteger(Data, Endian.Big, 0, 2, false));
        Assert.Equal(0x3412, ByteCodec.ReadInteger(Data, Endian.Little, 0, 2, false));
        Assert.Equal(0x12345678, ByteCodec.ReadInteger(Data, Endian.Big, 0, 4, false));
        Assert.Equal(0x78563412, ByteCodec.ReadInteger(Data, Endian.Little, 0, 4, false));
        Assert.Equal(0xFF, ByteCodec.ReadInteger(Data, Endian.Big, 4, 1, false));
        Assert.Equal(-1, ByteCodec.ReadInteger(Data, Endian.Big, 4, 1, true));
        Assert.Equal(-2, ByteCodec.ReadInteger(Data, Endian.Big, 4, 2, true));
        Assert.Equal(0xFFFE, ByteCodec.ReadInteger(Data, Endian.Big, 4, 2, false));
        Assert.Equal(0xFFFE8000L, ByteCodec.ReadInteger(Data, Endian.Big, 4, 4, false));
    }

    [Fact]
    public void ReadsMaskedBits()
    {
        // 0x56 = 0101 0110; mask 0x0C selects bits 2-3 = 01.
        Assert.Equal(1, ByteCodec.ReadMasked(Data, Endian.Big, 2, 1, false, 0x0C));
        Assert.Equal(0x5, ByteCodec.ReadMasked(Data, Endian.Big, 2, 1, false, 0xF0));
    }

    [Fact]
    public void OutOfRangeThrows()
    {
        Assert.Throws<IndexOutOfRangeException>(() => ByteCodec.ReadInteger(Data, Endian.Big, 7, 2, false));
    }

    [Theory]
    [InlineData(1, Endian.Big, 0x7F)]
    [InlineData(2, Endian.Big, 0x1234)]
    [InlineData(2, Endian.Little, 0x1234)]
    [InlineData(4, Endian.Big, 0x12345678)]
    [InlineData(4, Endian.Little, -5)]
    public void IntegerRoundTrip(int storageType, Endian endian, int value)
    {
        var bytes = ByteCodec.EncodeInteger(value, endian, storageType);
        Assert.Equal(storageType, bytes.Length);
        Assert.Equal(value, (int)ByteCodec.ReadInteger(bytes, endian, 0, storageType, true));
    }

    [Fact]
    public void FloatsAreBigEndianUnlessMemModelSaysLittle()
    {
        var big = ByteCodec.EncodeFloat(1.5f, null);
        Assert.Equal(new byte[] { 0x3F, 0xC0, 0, 0 }, big);
        Assert.Equal(1.5f, ByteCodec.ReadFloat(big, 0, null));
        var little = ByteCodec.EncodeFloat(1.5f, Endian.Little);
        Assert.Equal(new byte[] { 0, 0, 0xC0, 0x3F }, little);
        Assert.Equal(1.5f, ByteCodec.ReadFloat(little, 0, Endian.Little));
    }

    [Theory]
    [InlineData(StorageType.Movi20, 0x12345)]
    [InlineData(StorageType.Movi20, -0x12345)]
    [InlineData(StorageType.Movi20S, 0x123400)]
    public void Movi20RoundTrip(int storageType, int value)
    {
        // Opcode byte before the immediate (register field), which must not be touched.
        var image = new byte[] { 0x0A, 0, 0, 0 };
        var encoded = ByteCodec.EncodeInteger(value, Endian.Big, storageType);
        Array.Copy(encoded, 0, image, 1, 3);
        Assert.Equal(value, ByteCodec.ReadInteger(image, Endian.Big, 1, storageType, true));
        Assert.Equal(0x0A, image[0]);
    }

    [Theory]
    [InlineData("15540b", 15540)]
    [InlineData("2kb", 2048)]
    [InlineData("1mb", 1048576)]
    [InlineData("512kb", 524288)]
    [InlineData("1024", 1024)]
    public void ParsesFileSizes(string text, int expected) => Assert.Equal(expected, ByteCodec.ParseFileSize(text));

    [Theory]
    [InlineData("0x3C8E", 0x3C8E)]
    [InlineData("3C8E", 0x3C8E)]
    [InlineData("-1", -1)]
    [InlineData("0xFFFFFFFF", -1)]
    public void ParsesHex(string text, int expected) => Assert.Equal(expected, HexUtil.HexToInt(text));

    [Theory]
    [InlineData("uint8", 1, false)]
    [InlineData("int16", 2, true)]
    [InlineData("uint32", 4, false)]
    [InlineData("float", StorageType.Float, false)]
    [InlineData("movi20", StorageType.Movi20, true)]
    [InlineData("movi20s", StorageType.Movi20S, true)]
    public void ParsesStorageTypes(string text, int type, bool signed)
    {
        Assert.Equal(type, StorageType.Parse(text));
        Assert.Equal(signed, StorageType.IsSigned(text));
    }

    [Theory]
    [InlineData(12.345, "0.00", "12.35")]
    [InlineData(12.0, "0", "12")]
    [InlineData(0.0, "#.##", "0")]
    [InlineData(1234.5, "#,##0.0", "1,234.5")]
    [InlineData(255, "0x%04X", "0x00FF")]
    [InlineData(10, "%d rpm", "10 rpm")]
    [InlineData(1.5, "%.2f", "1.50")]
    public void FormatsValues(double value, string pattern, string expected)
    {
        Assert.Equal(expected, ValueFormatter.Format(value, pattern, CultureInfo.InvariantCulture));
    }
}
