using System.Xml.Linq;
using RaiderLite.Core.Checksums;
using RaiderLite.Core.Model;

namespace RaiderLite.Core.Definitions;

/// <summary>
/// Builds a <see cref="Rom"/> from a &lt;rom&gt; element and its base chain.
/// Port of RomRaider's DOMRomUnmarshaller and TableScaleUnmarshaller.
/// </summary>
public sealed class RomUnmarshaller
{
    private readonly DefinitionDocument _document;
    private readonly Dictionary<string, int> _tableAddresses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Scale> _scales = new(StringComparer.Ordinal);
    private readonly List<string> _warnings = [];
    private Endian? _memModelEndian;

    private sealed class TableOmittedException : Exception;

    public RomUnmarshaller(DefinitionDocument document)
    {
        _document = document;
    }

    /// <summary>Loads the definition for <paramref name="romNode"/> and reads <paramref name="image"/>.</summary>
    public static Rom Load(DefinitionDocument document, XElement romNode, byte[] image, string? imagePath = null)
    {
        var rom = new RomUnmarshaller(document).Unmarshal(romNode);
        rom.DefinitionPath = document.Path;
        rom.FilePath = imagePath ?? "";
        rom.RomId.RamOffset = rom.RomId.FileSize - image.Length;
        rom.Populate(image);
        return rom;
    }

    /// <summary>Builds the rom's tables (not yet populated from an image).</summary>
    public Rom Unmarshal(XElement romNode)
    {
        // Resolve the inheritance chain, leaf first.
        var chain = new List<XElement>();
        var visited = new HashSet<XElement>();
        for (var node = romNode; ;)
        {
            if (!visited.Add(node))
                throw new DefinitionException("The definition's base roms form a loop.");
            chain.Add(node);
            var baseId = Xml.Attr(node, "base");
            if (baseId == null || baseId.Equals("none", StringComparison.OrdinalIgnoreCase))
                break;
            node = _document.FindByXmlId(baseId)?.Node
                   ?? throw new DefinitionException($"Base rom \"{baseId}\" was not found in {_document}.");
        }

        UnmarshalBaseScales(_document.Root);

        // RomRaider registers table names (and the address to use) from every rom in
        // the chain before creating tables; the most derived rom's address wins.
        foreach (var node in chain)
            FilterFoundRomTables(node);

        var rom = new Rom(new RomId());
        for (int i = chain.Count - 1; i >= 0; i--)
            UnmarshalRomBody(chain[i], rom);

        rom.Warnings.AddRange(_warnings);
        return rom;
    }

    private void UnmarshalBaseScales(XElement root)
    {
        foreach (var n in root.Elements().Where(e => Xml.Is(e, "scalingbase")))
            UnmarshalScale(n, new Scale());
    }

    private void FilterFoundRomTables(XElement romNode)
    {
        foreach (var n in romNode.Elements().Where(e => Xml.Is(e, "table")))
        {
            if (Xml.Bool(Xml.Attr(n, "omit"), false))
                continue;
            var name = Xml.Attr(n, "name") ?? "unknown";
            int address = ParseAddress(Xml.Attr(n, "storageaddress") ?? "-1", name);
            if (!_tableAddresses.TryGetValue(name, out var existing))
            {
                if (address >= 0)
                    _tableAddresses[name] = address;
            }
            else if (existing < 1 && address >= 0)
            {
                _tableAddresses[name] = address;
            }
        }
    }

    private int ParseAddress(string text, string tableName)
    {
        try
        {
            return HexUtil.HexToInt(text);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            _warnings.Add($"{tableName}: invalid storage address \"{text}\".");
            return -1;
        }
    }

    private void UnmarshalRomBody(XElement romNode, Rom rom)
    {
        rom.RomId.Obsolete = false;
        foreach (var n in romNode.Elements())
        {
            if (Xml.Is(n, "romid"))
            {
                ParseRomId(n, rom.RomId, e => _memModelEndian = e, _warnings.Add);
            }
            else if (Xml.Is(n, "table"))
            {
                var name = Xml.Attr(n, "name");
                var existing = name != null ? rom.GetTable(name) : null;
                try
                {
                    var table = UnmarshalTable(n, existing, rom);
                    if (table != null)
                        rom.AddTable(table);
                }
                catch (TableOmittedException)
                {
                    if (existing != null)
                        rom.RemoveTable(existing);
                }
                catch (Exception ex) when (ex is DefinitionException or FormatException or OverflowException or ArgumentException)
                {
                    _warnings.Add($"{name ?? "(unnamed table)"}: {ex.Message}");
                }
            }
            else if (Xml.Is(n, "checksum"))
            {
                var attrs = n.Attributes().ToDictionary(a => a.Name.LocalName.ToLowerInvariant(), a => a.Value);
                rom.RomId.Checksum = attrs.GetValueOrDefault("type", "");
                try
                {
                    var manager = ChecksumFactory.Create(attrs);
                    if (manager != null)
                        rom.ChecksumManagers.Add(manager);
                    else
                        _warnings.Add($"Checksum type \"{rom.RomId.Checksum}\" is not supported; checksums will not be updated.");
                }
                catch (Exception ex) when (ex is FormatException or OverflowException)
                {
                    _warnings.Add($"Checksum \"{rom.RomId.Checksum}\": {ex.Message}");
                }
            }
        }
    }

    /// <summary>Overlays the fields of a &lt;romid&gt; element onto <paramref name="romId"/>.</summary>
    internal static void ParseRomId(XElement romIdNode, RomId romId, Action<Endian?> memModelEndian, Action<string>? warn = null)
    {
        foreach (var n in romIdNode.Elements())
        {
            var text = Xml.Text(n);
            switch (n.Name.LocalName.ToLowerInvariant())
            {
                case "xmlid": romId.XmlId = text; break;
                case "internalidaddress":
                    // An unreadable address simply never matches.
                    romId.InternalIdAddress = HexUtil.TryHexToInt(text, out int idAddress) ? idAddress : -1;
                    break;
                case "internalidstring": romId.InternalIdString = text; break;
                case "author": romId.Author = text; break;
                case "version": romId.Version = text; break;
                case "caseid": romId.CaseId = text; break;
                case "ecuid": romId.EcuId = text; break;
                case "make": romId.Make = text; break;
                case "market": romId.Market = text; break;
                case "model": romId.Model = text; break;
                case "submodel": romId.SubModel = text; break;
                case "transmission": romId.Transmission = text; break;
                case "year": romId.Year = text; break;
                case "noramoffset": romId.DisableRamOffset(); break;
                case "offset":
                    if (HexUtil.TryHexToInt(text, out int offset))
                        romId.SetOffset(offset);
                    else
                        warn?.Invoke($"romid: invalid offset \"{text}\" ignored.");
                    break;
                case "flashmethod": romId.FlashMethod = text; break;
                case "memmodel":
                    romId.MemModel = text;
                    var endian = Xml.Attr(n, "endian");
                    if (endian != null)
                        memModelEndian(endian.Equals("little", StringComparison.OrdinalIgnoreCase) ? Endian.Little : Endian.Big);
                    break;
                case "filesize":
                    try { romId.FileSize = ByteCodec.ParseFileSize(text); }
                    catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
                    {
                        warn?.Invoke($"romid: invalid filesize \"{text}\" ignored.");
                    }
                    break;
                case "obsolete": romId.Obsolete = Xml.Bool(text, false); break;
            }
        }
    }

    private static Endian ParseEndian(string s) =>
        s.Equals("big", StringComparison.OrdinalIgnoreCase) || s == "2" ? Endian.Big : Endian.Little;

    private static AxisKind ParseAxisKind(string? type)
    {
        if (type == null)
            return AxisKind.None;
        if (type.Equals("X Axis", StringComparison.OrdinalIgnoreCase) || type.Equals("Static X Axis", StringComparison.OrdinalIgnoreCase) || type == "4")
            return AxisKind.X;
        if (type.Equals("Y Axis", StringComparison.OrdinalIgnoreCase) || type.Equals("Static Y Axis", StringComparison.OrdinalIgnoreCase) || type == "5")
            return AxisKind.Y;
        return AxisKind.None;
    }

    private static Table CreateTable(string type, string name)
    {
        switch (type.ToLowerInvariant())
        {
            case "3d": return new Table3D();
            case "2d": return new Table2D();
            case "1d": return new Table1D();
            case "x axis":
            case "static x axis": return new Table1D { AxisKind = AxisKind.X };
            case "y axis":
            case "static y axis": return new Table1D { AxisKind = AxisKind.Y };
            case "switch": return new TableSwitch();
            case "bitwiseswitch": return new TableBitwiseSwitch();
            case "none": throw new DefinitionException($"Table type unspecified for \"{name}\".");
            default: throw new DefinitionException($"Table type \"{type}\" unknown for \"{name}\".");
        }
    }

    /// <summary>
    /// Finds or creates the table a top-level &lt;table&gt; element describes and applies the element
    /// to it. Returns null when the table is skipped (no storage address anywhere in the chain).
    /// </summary>
    private Table? UnmarshalTable(XElement node, Table? table, Rom rom)
    {
        table = BaseTable(node, rom) ?? table;
        if (table == null)
        {
            var tn = Xml.Attr(node, "name") ?? "unknown";
            var type = Xml.Attr(node, "type") ?? "none";
            // Tables with no storage address anywhere in the chain are not created.
            if (!_tableAddresses.ContainsKey(tn) && !type.Contains("xis", StringComparison.Ordinal))
                return null;
            table = CreateTable(type, tn);
        }

        ApplyTableNode(node, table, rom, topLevel: true);
        return table;
    }

    /// <summary>
    /// Throws <see cref="TableOmittedException"/> for omit="true"; returns a copy of the table
    /// named by base="...", or null when there is none.
    /// </summary>
    private Table? BaseTable(XElement node, Rom rom)
    {
        if (Xml.Bool(Xml.Attr(node, "omit"), false))
            throw new TableOmittedException();

        var baseName = Xml.Attr(node, "base");
        if (baseName == null || baseName.Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;
        var baseTable = rom.GetTable(baseName);
        if (baseTable == null)
            _warnings.Add($"{Xml.Attr(node, "name")}: base table \"{baseName}\" not found.");
        return baseTable?.Clone();
    }

    /// <summary>The axis an axis element applies to: <paramref name="axis"/>, or a copy of its base.</summary>
    private Table1D AxisFor(XElement node, Table1D axis, Rom rom)
    {
        var baseTable = BaseTable(node, rom);
        if (baseTable is Table1D baseAxis)
            return baseAxis;
        if (baseTable != null)
            _warnings.Add($"{Xml.Attr(node, "name")}: base \"{baseTable.Name}\" is not a 1D table; ignored.");
        return axis;
    }

    /// <summary>Applies a &lt;table&gt; element's attributes and children to an existing table or axis.</summary>
    private void ApplyTableNode(XElement node, Table table, Rom rom, bool topLevel)
    {
        var name = Xml.Attr(node, "name") ?? table.Name;
        table.Name = name;

        if (Xml.Bool(Xml.Attr(node, "beforeram"), false))
            table.BeforeRam = true;

        if (Xml.Attr(node, "dataLayout") is { } layout)
            table.DataLayout = layout.Trim().Equals("bosch_subtract", StringComparison.OrdinalIgnoreCase) ? DataLayout.BoschSubtract : DataLayout.Default;

        if (Xml.Attr(node, "category") is { } category)
            table.Category = category;

        var storageType = Xml.Attr(node, "storagetype");
        if (storageType != null)
        {
            if (table.StorageType < 1)
                table.Signed = StorageType.IsSigned(storageType);
            table.StorageType = StorageType.Parse(storageType);
        }

        if (_memModelEndian == null)
        {
            if (Xml.Attr(node, "endian") is { } endian)
                table.Endian = ParseEndian(endian);
        }
        else
        {
            table.MemModelEndian = _memModelEndian;
            table.Endian = _memModelEndian.Value;
        }

        // RomRaider also applies the name lookup to axes, which can give an axis the
        // address of an unrelated top-level table with the same name; only top-level
        // tables use it here.
        if (topLevel && _tableAddresses.TryGetValue(name, out var address))
            table.StorageAddress = address;
        else if (Xml.Attr(node, "storageaddress") is { } sa)
            table.StorageAddress = ParseAddress(sa, name);

        if (Xml.Attr(node, "description") is { } description)
            table.Description = description;

        if ((Xml.Attr(node, "sizey") ?? Xml.Attr(node, "sizex")) is { } size)
            table.DataSize = Xml.Int(size, table.DataSize);

        table.Flip = Xml.Bool(Xml.Attr(node, "flipy") ?? Xml.Attr(node, "flipx"), table.Flip);
        table.UserLevel = Xml.Int(Xml.Attr(node, "userlevel"), table.UserLevel);
        table.Locked = Xml.Bool(Xml.Attr(node, "locked"), table.Locked);
        table.LogParam = Xml.Attr(node, "logparam") ?? table.LogParam;
        table.SetStringMask(Xml.Attr(node, "mask") ?? "");

        if (table is Table3D t3)
        {
            t3.SwapXY = Xml.Bool(Xml.Attr(node, "swapxy"), t3.SwapXY);
            t3.FlipX = Xml.Bool(Xml.Attr(node, "flipx"), t3.FlipX);
            t3.FlipY = Xml.Bool(Xml.Attr(node, "flipy"), t3.FlipY);
            t3.SizeX = Xml.Int(Xml.Attr(node, "sizex"), t3.SizeX);
            t3.SizeY = Xml.Int(Xml.Attr(node, "sizey"), t3.SizeY);
            t3.SkipCells = Xml.Int(Xml.Attr(node, "skipCells"), t3.SkipCells);
        }

        // Static axis labels are replaced, not appended, when a node redefines them.
        if (node.Elements().Any(e => Xml.Is(e, "data")))
            table.ClearStaticData();

        foreach (var n in node.Elements())
        {
            if (Xml.Is(n, "table"))
            {
                var axisKind = ParseAxisKind(Xml.Attr(n, "type"));
                if (table is Table2D t2 && axisKind != AxisKind.None)
                {
                    var axis = AxisFor(n, t2.Axis, rom);
                    ApplyTableNode(n, axis, rom, topLevel: false);
                    axis.AxisKind = axisKind;
                    axis.DataSize = t2.DataSize;
                    t2.Axis = axis;
                }
                else if (table is Table3D t3d && axisKind == AxisKind.X)
                {
                    var axis = AxisFor(n, t3d.XAxis, rom);
                    ApplyTableNode(n, axis, rom, topLevel: false);
                    axis.DataSize = t3d.SizeX;
                    t3d.XAxis = axis;
                }
                else if (table is Table3D t3e && axisKind == AxisKind.Y)
                {
                    var axis = AxisFor(n, t3e.YAxis, rom);
                    ApplyTableNode(n, axis, rom, topLevel: false);
                    axis.DataSize = t3e.SizeY;
                    t3e.YAxis = axis;
                }
            }
            else if (Xml.Is(n, "scaling"))
            {
                var baseScale = table.GetScale(Xml.Attr(n, "category") ?? "Default");
                table.AddScale(UnmarshalScale(n, baseScale));
            }
            else if (Xml.Is(n, "data"))
            {
                if (table is Table1D)
                    table.AddStaticDataCell(Xml.Text(n));
            }
            else if (Xml.Is(n, "description"))
            {
                table.Description = Xml.Text(n);
            }
            else if (Xml.Is(n, "state"))
            {
                table.AddPresetValue(Xml.Attr(n, "name") ?? "", Xml.Attr(n, "data") ?? "0");
            }
            else if (Xml.Is(n, "bit"))
            {
                var position = Xml.Attr(n, "position") ?? "";
                int bits = 8 * StorageType.ByteLength(table.StorageType);
                if (table is TableBitwiseSwitch &&
                    !(int.TryParse(position, out int bit) && bit >= 0 && bit < bits))
                {
                    _warnings.Add($"{table.Name}: bit \"{Xml.Attr(n, "name")}\" has position \"{position}\" outside 0..{bits - 1}; ignored.");
                    continue;
                }
                table.SetPresetValues(Xml.Attr(n, "name") ?? "", position);
            }
        }

    }

    private Scale UnmarshalScale(XElement node, Scale scale)
    {
        var baseName = Xml.Attr(node, "base") is { } b && !b.Equals("none", StringComparison.OrdinalIgnoreCase) ? b : null;
        bool hasBase = baseName != null;
        if (baseName != null)
        {
            if (_scales.TryGetValue(baseName.ToLowerInvariant(), out var baseScale))
                scale = baseScale.Clone();
            else
                _warnings.Add($"Scaling base \"{baseName}\" not found.");
        }
        else
        {
            scale.Category = Xml.Attr(node, "category") ?? "Default";
        }

        // A scaling with no name is named after its units.
        if (!scale.Category.Equals(Scale.RawValueCategory, StringComparison.OrdinalIgnoreCase) &&
            scale.Name.Equals(Scale.RawValueCategory, StringComparison.OrdinalIgnoreCase))
        {
            scale.Name = Xml.Attr(node, "name") ?? Xml.Attr(node, "units") ?? "none";
        }

        // RomRaider clears to_byte when the attribute is missing even if the scaling
        // inherits from a scalingbase; keep the inherited one in that case.
        var toByte = Xml.Attr(node, "to_byte");
        if (toByte != null || !hasBase)
            scale.ByteExpression = toByte;

        foreach (var attr in node.Attributes())
        {
            var value = attr.Value;
            try
            {
                switch (attr.Name.LocalName.ToLowerInvariant())
                {
                    case "units": scale.Unit = value; break;
                    case "expression": scale.Expression = value; break;
                    case "format": scale.Format = value; break;
                    case "max": scale.Max = Xml.Double(value); break;
                    case "min": scale.Min = Xml.Double(value); break;
                    case "coarseincrement":
                    case "increment": scale.CoarseIncrement = Xml.Double(value); break;
                    case "fineincrement": scale.FineIncrement = Xml.Double(value); break;
                }
            }
            catch (FormatException)
            {
                _warnings.Add($"Scaling \"{scale.Name}\": invalid {attr.Name.LocalName} \"{value}\".");
            }
        }

        foreach (var expr in new[] { scale.Expression, scale.ByteExpression })
        {
            if (expr != null && Expressions.ExpressionEngine.Validate(expr) is { } error)
                _warnings.Add($"Scaling \"{scale.Name}\": {error}");
        }

        _scales[scale.Name.ToLowerInvariant()] = scale;
        return scale;
    }
}
