using System.Text;

namespace RaiderLite.Core.Model;

/// <summary>Identification block of a ROM definition (the &lt;romid&gt; element).</summary>
public sealed class RomId
{
    private int _ramOffset;

    public string XmlId { get; set; } = "";
    public int InternalIdAddress { get; set; }
    public string InternalIdString { get; set; } = "";
    public string CaseId { get; set; } = "";
    public string EcuId { get; set; } = "";
    public string Make { get; set; } = "";
    public string Market { get; set; } = "";
    public string Model { get; set; } = "";
    public string SubModel { get; set; } = "";
    public string Transmission { get; set; } = "";
    public string Year { get; set; } = "Unknown";
    public string FlashMethod { get; set; } = "";
    public string MemModel { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "";
    public string Checksum { get; set; } = "";
    public string EditStamp { get; set; } = "";
    public int FileSize { get; set; }
    public bool Obsolete { get; set; }
    public bool NoRamOffset { get; private set; }

    /// <summary>Subtracted from table addresses to get file offsets.</summary>
    public int RamOffset
    {
        get => _ramOffset;
        set
        {
            if (!NoRamOffset)
                _ramOffset = value;
        }
    }

    public void DisableRamOffset()
    {
        NoRamOffset = true;
        _ramOffset = 0;
    }

    /// <summary>The &lt;offset&gt; element: a fixed offset that disables the computed RAM offset.</summary>
    public void SetOffset(int offset)
    {
        _ramOffset = -offset;
        NoRamOffset = true;
    }

    /// <summary>Whether <paramref name="file"/> carries this definition's internal ID.</summary>
    public bool CheckMatch(byte[] file)
    {
        if (string.IsNullOrEmpty(InternalIdString))
            return false;

        // Both fields set to "force": use this definition no matter what.
        if (InternalIdAddress == -1 && InternalIdString.Equals("force", StringComparison.OrdinalIgnoreCase))
            return true;
        if (InternalIdAddress < 0)
            return false;

        if (InternalIdString.Length > 2 && InternalIdString.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            byte[] idBytes;
            try
            {
                idBytes = HexUtil.HexStringToBytes(InternalIdString[2..]);
            }
            catch (FormatException)
            {
                return false;
            }
            if (file.Length < InternalIdAddress + idBytes.Length)
                return false;
            return file.AsSpan(InternalIdAddress, idBytes.Length).SequenceEqual(idBytes);
        }

        if (file.Length < InternalIdAddress + InternalIdString.Length)
            return false;
        var ecuId = Encoding.Latin1.GetString(file, InternalIdAddress, InternalIdString.Length);
        return ecuId.Equals(InternalIdString, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// How many bytes of the image the ID checks: when several definitions match (one ID can be a
    /// prefix of another, e.g. "LOTUS EVORA " and "LOTUS EVORA GT430 ..."), the longest is the most specific.
    /// </summary>
    public int MatchLength =>
        InternalIdString.Length > 2 && InternalIdString.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? (InternalIdString.Length - 2) / 2
            : InternalIdAddress < 0 ? 0 : InternalIdString.Length;

    public RomId Clone() => (RomId)MemberwiseClone();

    public override string ToString()
    {
        var parts = new[] { Make, Model, SubModel, Market, Transmission }.Where(s => !string.IsNullOrWhiteSpace(s));
        var desc = string.Join(" ", parts);
        return desc.Length > 0 ? $"{XmlId} ({desc})" : XmlId;
    }
}
