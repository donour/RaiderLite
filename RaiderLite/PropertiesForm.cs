using System.Globalization;
using RaiderLite.Core.Model;

namespace RaiderLite;

/// <summary>Read-only key/value list plus a notes box (table and image properties).</summary>
public sealed class PropertiesForm : DialogBase
{
    public PropertiesForm(string title, IEnumerable<(string Key, string Value)> properties, string? notes) : base(title, cancel: false)
    {
        FormBorderStyle = FormBorderStyle.Sizable;
        Size = new Size(620, 560);
        MinimumSize = new Size(400, 300);

        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            GridLines = true,
        };
        list.Columns.Add("Property", 170);
        list.Columns.Add("Value", 400);
        foreach (var (key, value) in properties)
            list.Items.Add(new ListViewItem([key, value]));

        // Copy selected rows with Ctrl+C.
        list.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.C && list.SelectedItems.Count > 0)
                Clipboard.SetText(string.Join(Environment.NewLine, list.SelectedItems.Cast<ListViewItem>().Select(i => $"{i.Text}\t{i.SubItems[1].Text}")));
        };

        if (!string.IsNullOrWhiteSpace(notes))
        {
            var box = new TextBox
            {
                Dock = DockStyle.Bottom,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Height = 150,
                Text = notes.Replace("\r\n", "\n").Replace("\n", Environment.NewLine),
            };
            Controls.Add(list);
            Controls.Add(box);
        }
        else
        {
            Controls.Add(list);
        }
        list.BringToFront();
    }

    public static IEnumerable<(string, string)> DescribeTable(Table table)
    {
        yield return ("Name", table.Name);
        yield return ("Category", table.Category);
        yield return ("Type", table switch
        {
            TableBitwiseSwitch => "Bitwise switch",
            TableSwitch => "Switch",
            Table3D => "3D",
            Table2D => "2D",
            _ => "1D",
        });
        yield return ("Shape", table.ShapeDescription);
        yield return ("Storage address", $"0x{table.StorageAddress:X}");
        if (table.RamOffset != 0)
            yield return ("File offset", $"0x{table.StorageAddress - table.RamOffset:X} (RAM offset 0x{table.RamOffset:X})");
        yield return ("Storage type", StorageType.Describe(table.StorageType, table.Signed));
        yield return ("Endian", table.MemModelEndian is { } mm ? $"{mm} (memory model)" : table.Endian.ToString());
        switch (table)
        {
            case Table3D t3:
                yield return ("Size", $"{t3.SizeX} x {t3.SizeY}");
                if (t3.SwapXY || t3.FlipX || t3.FlipY || t3.SkipCells != 0)
                    yield return ("Layout", $"swapxy={t3.SwapXY} flipx={t3.FlipX} flipy={t3.FlipY} skipCells={t3.SkipCells}");
                break;
            default:
                yield return ("Size", table.DataSize.ToString(CultureInfo.InvariantCulture));
                break;
        }
        if (table.BitMask != 0)
            yield return ("Mask", $"0x{table.BitMask:X}");
        if (table.DataLayout != DataLayout.Default)
            yield return ("Data layout", table.DataLayout.ToString());
        yield return ("User level", table.UserLevel.ToString(CultureInfo.InvariantCulture));
        if (table.Locked)
            yield return ("Locked", "yes");
        if (!string.IsNullOrEmpty(table.LogParam))
            yield return ("Log parameter", table.LogParam);

        foreach (var scale in table.Scales)
        {
            var mark = ReferenceEquals(scale, table.CurrentScale) ? " (current)" : "";
            yield return ($"Scale: {scale.Category}{mark}",
                $"{scale.Unit}: {scale.Expression}  |  to_byte: {scale.ByteExpression ?? "(approximated)"}  |  format {scale.Format}  |  fine {scale.FineIncrement}, coarse {scale.CoarseIncrement}");
        }

        foreach (var axis in table.Axes)
        {
            var label = axis.AxisKind == AxisKind.Y ? "Y axis" : "X axis";
            if (axis.IsStaticDataTable)
            {
                yield return (label, $"{axis.Name} (static): {string.Join(", ", axis.StaticTexts)}");
                continue;
            }
            var s = axis.CurrentScale;
            yield return (label, $"{axis.Name} at 0x{axis.StorageAddress:X}, {StorageType.Describe(axis.StorageType, axis.Signed)}, {axis.DataSize} values" +
                                 (!s.IsRaw ? $", {s.Unit}: {s.Expression}" : ""));
        }

        if (table.Presets.Presets.Count > 0)
        {
            foreach (var p in table.Presets.Presets)
                yield return (table is TableBitwiseSwitch ? "Bit" : "State", $"{p.Name}: {p.DataText}");
        }
    }

    public static IEnumerable<(string, string)> DescribeRom(Rom rom)
    {
        var id = rom.RomId;
        yield return ("File", rom.FilePath);
        yield return ("File size", $"{rom.Binary.Length} bytes (0x{rom.Binary.Length:X})");
        yield return ("Definition", rom.DefinitionPath ?? "");
        yield return ("XML ID", id.XmlId);
        yield return ("Internal ID", $"\"{id.InternalIdString}\" at 0x{id.InternalIdAddress:X}");
        foreach (var (k, v) in new[]
                 {
                     ("Make", id.Make), ("Market", id.Market), ("Model", id.Model), ("Submodel", id.SubModel),
                     ("Transmission", id.Transmission), ("Year", id.Year), ("ECU ID", id.EcuId), ("Case ID", id.CaseId),
                     ("Memory model", id.MemModel), ("Flash method", id.FlashMethod), ("Author", id.Author), ("Version", id.Version),
                 })
        {
            if (!string.IsNullOrEmpty(v) && v != "Unknown")
                yield return (k, v);
        }
        yield return ("Definition file size", id.FileSize > 0 ? $"{id.FileSize} bytes" : "(not specified)");
        yield return ("RAM offset", id.NoRamOffset && id.RamOffset == 0 ? "none" : $"0x{id.RamOffset:X}");
        yield return ("Tables", rom.Tables.Count.ToString(CultureInfo.InvariantCulture));
        if (rom.FaultyTables.Count > 0)
            yield return ("Unreadable tables", string.Join(", ", rom.FaultyTables));
        if (rom.ChecksumManagers.Count > 0)
        {
            yield return ("Checksums", string.Join(", ", rom.ChecksumManagers.Select(c => c.Type)));
            yield return ("Valid checksums", $"{rom.ValidateChecksums()} of {rom.TotalChecksums}");
        }
        else
        {
            yield return ("Checksums", "none defined");
        }
        yield return ("Unsaved changes", rom.IsDirty ? "yes" : "no");
    }
}
