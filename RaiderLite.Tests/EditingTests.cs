using RaiderLite.Core.Model;

namespace RaiderLite.Tests;

public class EditingTests
{
    public EditingTests()
    {
        CoreSettings.Current = new CoreSettings();
    }

    private static Rom Load() => TestData.LoadInline(SyntheticDefinitionTests.Xml, SyntheticDefinitionTests.MakeImage());

    [Fact]
    public void IncrementAndMultiplySelectedCells()
    {
        var rom = Load();
        var curve = (Table2D)rom.GetTable("Curve")!;
        curve.Cells[1].IsSelected = true;
        curve.Increment(curve.CurrentScale!.CoarseIncrement);
        Assert.Equal(15.0, curve.Cells[1].RealValue);
        Assert.Equal(5.0, curve.Cells[0].RealValue);
        curve.Multiply(2);
        Assert.Equal(30.0, curve.Cells[1].RealValue);
        curve.Increment(-curve.CurrentScale.FineIncrement);
        Assert.Equal(29.5, curve.Cells[1].RealValue);
    }

    [Fact]
    public void IncrementAlwaysMovesAtLeastOneStep()
    {
        var rom = Load();
        var scalar = rom.GetTable("Scalar")!;
        scalar.Cells[0].IsSelected = true;
        scalar.Increment(0.01); // smaller than one raw step (0.1)
        Assert.Equal(501, scalar.Cells[0].BinValue);
    }

    [Fact]
    public void SetValueAppliesToSelectionIncludingAxes()
    {
        var rom = Load();
        var map = (Table3D)rom.GetTable("Map")!;
        map.Data3D[0, 0].IsSelected = true;
        map.Data3D[2, 1].IsSelected = true;
        map.XAxis.Cells[1].IsSelected = true;
        map.SetRealValue("9");
        Assert.Equal(9, map.Data3D[0, 0].BinValue);
        Assert.Equal(9, map.Data3D[2, 1].BinValue);
        Assert.Equal(1, map.Data3D[1, 0].BinValue);
        Assert.Equal(9, map.XAxis.Cells[1].BinValue);
    }

    [Fact]
    public void InterpolateTwoDAlongAxis()
    {
        var rom = Load();
        var curve = (Table2D)rom.GetTable("Curve")!;
        curve.Cells[3].SetBinValue(100);
        curve.Cells[0].IsSelected = true;
        curve.Cells[3].IsSelected = true;
        curve.HorizontalInterpolate();
        // Axis raw 40,80,120,160 -> data 10 .. 100 linearly.
        Assert.Equal([10.0, 40, 70, 100], curve.Cells.Select(c => c.BinValue));
    }

    [Fact]
    public void InterpolateThreeD()
    {
        var rom = Load();
        var map = (Table3D)rom.GetTable("Map")!;
        // Row 0 is 0,1,2: set the ends to 0 and 100 then interpolate horizontally.
        map.Data3D[2, 0].SetBinValue(100);
        map.Data3D[0, 0].IsSelected = true;
        map.Data3D[2, 0].IsSelected = true;
        map.HorizontalInterpolate();
        Assert.Equal(50, map.Data3D[1, 0].BinValue);
    }

    [Fact]
    public void UndoRedoAndRevert()
    {
        var rom = Load();
        var curve = (Table2D)rom.GetTable("Curve")!;
        var original = (byte[])rom.Binary.Clone();

        using (rom.History.Begin("Set"))
        {
            curve.Cells[0].SetBinValue(1);
            curve.Cells[1].SetBinValue(2);
        }
        Assert.Equal("Set", rom.History.UndoName);
        rom.History.Undo();
        Assert.Equal(original, rom.Binary);
        Assert.Equal(10, curve.Cells[0].BinValue);
        rom.History.Redo();
        Assert.Equal(2, curve.Cells[1].BinValue);

        curve.Cells[1].IsSelected = true;
        curve.UndoSelected();
        Assert.Equal(20, curve.Cells[1].BinValue);
        Assert.Equal(1, curve.Cells[0].BinValue);
        curve.UndoAll();
        Assert.Equal(original, rom.Binary);

        curve.Cells[0].SetBinValue(7);
        curve.SetRevertPoint();
        curve.Cells[0].SetBinValue(8);
        curve.UndoAll();
        Assert.Equal(7, curve.Cells[0].BinValue);
    }

    [Fact]
    public void DataChangedFiresOnParentForAxisEdits()
    {
        var rom = Load();
        var curve = (Table2D)rom.GetTable("Curve")!;
        int fired = 0;
        curve.DataChanged += (_, _) => fired++;
        curve.Axis.Cells[0].SetBinValue(1);
        Assert.True(fired > 0);
    }

    [Fact]
    public void CompareAgainstAnotherTable()
    {
        var a = Load();
        var b = Load();
        var ta = (Table3D)a.GetTable("Map")!;
        var tb = (Table3D)b.GetTable("Map")!;
        tb.Data3D[1, 1].SetBinValue(10);
        ta.SetCompareTable(tb);
        Assert.Equal(4 - 10, ta.Data3D[1, 1].CompareValue);
        Assert.Equal(0, ta.Data3D[0, 0].CompareValue);
        ta.CompareValueType = CompareValueType.Original;
        ta.RefreshCompare();
        Assert.Equal(0, ta.Data3D[1, 1].CompareValue);
        ta.SetCompareTable(null);
        Assert.Equal(4, ta.Data3D[1, 1].CompareValue);

        ta.Data3D[0, 1].SetBinValue(50);
        ta.CompareToOriginal();
        Assert.Equal(50 - 3, ta.Data3D[0, 1].CompareValue);
        Assert.Equal(0, ta.Data3D[1, 1].CompareValue);
    }

    [Fact]
    public void BracketFindsBreakpointsEitherSide()
    {
        double[] up = [10, 20, 40];
        Assert.Equal((0, 0, 0.0), Table.Bracket(up, 5));
        Assert.Equal((1, 1, 0.0), Table.Bracket(up, 20));
        Assert.Equal((1, 2, 0.25), Table.Bracket(up, 25));
        Assert.Equal((2, 2, 0.0), Table.Bracket(up, 99));
        double[] down = [40, 20, 10];
        Assert.Equal((0, 0, 0.0), Table.Bracket(down, 50));
        Assert.Equal((1, 2, 0.5), Table.Bracket(down, 15));
        Assert.Equal((2, 2, 0.0), Table.Bracket(down, 0));
        Assert.Equal((0, 0, 0.0), Table.Bracket([7.0], 3));
    }

    [Fact]
    public void InterpolatedCompareLooksUpAtThisTablesAxis()
    {
        var a = Load();
        var b = Load();
        var ca = (Table2D)a.GetTable("Curve")!;   // axis 40, 80, 120, 160; data 10..40
        var cb = (Table2D)b.GetTable("Curve")!;
        for (int i = 0; i < 4; i++)
            cb.Axis.Cells[i].SetBinValue(60 + 40 * i);
        ca.SetCompareTable(cb);
        Assert.All(ca.Cells, c => Assert.Equal(0, c.CompareValue));

        ca.InterpolateCompare = true;
        ca.RefreshCompare();
        // 40 is below b's axis (held at its first value); 80 is halfway between 60 and 100, ...
        Assert.Equal([10.0, 15, 25, 35], ca.Cells.Select(c => c.CompareToValue));
        Assert.Equal([0.0, 5, 5, 5], ca.Cells.Select(c => c.CompareValue));
        // The axis itself is still compared position by position.
        Assert.Equal(40 - 60, ca.Axis.Cells[0].CompareValue);
    }

    [Fact]
    public void InterpolatedCompareOfMapIsBilinear()
    {
        var a = Load();
        var b = Load();
        var ma = (Table3D)a.GetTable("Map")!;   // X 1, 2, 3; Y 10, 20; data[x, y] = 3y + x
        var mb = (Table3D)b.GetTable("Map")!;
        ma.InterpolateCompare = true;
        ma.SetCompareTable(mb);
        Assert.All(ma.DataCells, c => Assert.Equal(0, c.CompareValue));

        for (int i = 0; i < 3; i++)
            mb.XAxis.Cells[i].SetBinValue(2 * i);
        mb.YAxis.Cells[1].SetBinValue(30);
        ma.RefreshCompare();
        Assert.Equal(0.5, ma.Data3D[0, 0].CompareToValue);  // x 1 of 0..2, y 10 = first row
        Assert.Equal(3.0, ma.Data3D[2, 1].CompareToValue);  // x 3 of 2..4, y 20 of 10..30
        Assert.Equal(5 - 3.0, ma.Data3D[2, 1].CompareValue);
    }

    [Fact]
    public void InterpolatedChangesShowWhatAnAxisEditDoesToLookups()
    {
        var curve = (Table2D)Load().GetTable("Curve")!;
        curve.InterpolateCompare = true;
        curve.Axis.Cells[1].SetBinValue(100);   // was 80: halfway to 120 in the original
        curve.CompareToOriginal();
        Assert.Equal(25, curve.Cells[1].CompareToValue);
        Assert.Equal(20 - 25, curve.Cells[1].CompareValue);
        Assert.Equal(0, curve.Cells[2].CompareValue);
    }

    [Fact]
    public void InterpolatedCompareWithStaticAxisGoesByPosition()
    {
        var a = Load();
        var b = Load();
        var sa = (Table2D)a.GetTable("StaticCurve")!;
        var sb = (Table2D)b.GetTable("StaticCurve")!;
        sb.Cells[1].SetBinValue(9);
        sa.InterpolateCompare = true;
        sa.SetCompareTable(sb);
        Assert.Equal([-2.0, 9, 5], sa.Cells.Select(c => c.CompareToValue));
    }

    [Fact]
    public void CopyTableAndPasteBackIsNoChange()
    {
        var rom = Load();
        foreach (var name in new[] { "Scalar", "Curve", "StaticCurve", "Map" })
        {
            var t = rom.GetTable(name)!;
            var text = TableClipboard.CopyTable(t);
            var before = (byte[])rom.Binary.Clone();
            TableClipboard.Paste(t, text);
            Assert.Equal(before, rom.Binary);
        }
    }

    [Fact]
    public void PasteRestoresCopiedTable()
    {
        var a = Load();
        var b = Load();
        var map = (Table3D)a.GetTable("Map")!;
        var text = TableClipboard.CopyTable(map);
        Assert.StartsWith("[Table3D]", text);

        var target = (Table3D)b.GetTable("Map")!;
        foreach (var c in target.AllCells)
            c.SetBinValue(99);
        TableClipboard.Paste(target, text);
        Assert.Equal(map.AllCells.Select(c => c.BinValue), target.AllCells.Select(c => c.BinValue));
    }

    [Fact]
    public void CopyAndPasteSelection3D()
    {
        var rom = Load();
        var map = (Table3D)rom.GetTable("Map")!;
        map.Data3D[0, 0].IsSelected = true;
        map.Data3D[1, 1].IsSelected = true;
        var text = TableClipboard.CopySelection(map)!;
        var lines = text.Split(Environment.NewLine);
        Assert.Equal("[Selection3D]", lines[0]);
        Assert.Equal("0.0\tx", lines[1]);
        Assert.Equal("x\t4.0", lines[2]);

        map.ClearSelection();
        map.Data3D[1, 0].IsSelected = true;
        TableClipboard.Paste(map, text);
        Assert.Equal(0, map.Data3D[1, 0].BinValue);   // pasted
        Assert.Equal(2, map.Data3D[2, 0].BinValue);   // "x" skipped
        Assert.Equal(4, map.Data3D[2, 1].BinValue);   // pasted
    }

    [Fact]
    public void PastePlainSpreadsheetBlock()
    {
        var rom = Load();
        var map = (Table3D)rom.GetTable("Map")!;
        map.Data3D[1, 0].IsSelected = true;
        TableClipboard.Paste(map, "7\t8\r\n9\t10\r\n");
        Assert.Equal(7, map.Data3D[1, 0].BinValue);
        Assert.Equal(8, map.Data3D[2, 0].BinValue);
        Assert.Equal(9, map.Data3D[1, 1].BinValue);
        Assert.Equal(10, map.Data3D[2, 1].BinValue);
        // One undo step for the whole paste.
        rom.History.Undo();
        Assert.Equal(1, map.Data3D[1, 0].BinValue);
        Assert.Equal(5, map.Data3D[2, 1].BinValue);
    }

    [Fact]
    public void PasteSelection1DIntoTwoDData()
    {
        var rom = Load();
        var curve = (Table2D)rom.GetTable("Curve")!;
        curve.Cells[2].IsSelected = true;
        TableClipboard.Paste(curve, "[Selection1D]\r\n1.5\t2.5");
        Assert.Equal(3, curve.Cells[2].BinValue);
        Assert.Equal(5, curve.Cells[3].BinValue);
    }

    [Fact]
    public void BrokenToByteFallsBackToApproximation()
    {
        const string xml = """
            <roms><rom>
              <romid><xmlid>R</xmlid><noRamOffset/></romid>
              <table name="T" type="1D" storageaddress="0x0" storagetype="uint8">
                <scaling units="l" expression="((x/20)+5)/14.6" to_byte="((x14.6)-5)*20" format="0.00" fineincrement="0.01" coarseincrement="0.1"/>
              </table>
            </rom></roms>
            """;
        var image = new byte[4];
        var rom = TestData.LoadInline(xml, image);
        Assert.Contains(rom.Warnings, w => w.Contains("x14", StringComparison.Ordinal));
        var cell = rom.GetTable("T")!.Cells[0];
        cell.SetRealValue(((100.0 / 20) + 5) / 14.6);
        Assert.Equal(100, image[0]);
        cell.IsSelected = true;
        cell.Table.Increment(0.1);
        Assert.True(image[0] > 100);
    }

    [Fact]
    public void AxisScalingIsIndependentOfValueScaling()
    {
        var rom = Load();
        var curve = (Table2D)rom.GetTable("Curve")!;
        int scaleEvents = 0;
        curve.ScaleChanged += (_, _) => scaleEvents++;

        // Axis to raw on its own: values keep their scaling.
        var axisRaw = curve.Axis.Scales.Single(s => s.IsRaw);
        curve.Axis.SetCurrentScale(axisRaw);
        Assert.Equal(40, curve.Axis.Cells[0].RealValue);
        Assert.Equal(5.0, curve.Cells[0].RealValue);
        Assert.True(scaleEvents > 0); // the parent table hears about axis changes

        // Axis back to RPM, values to raw: the axis can be set back independently.
        curve.Axis.SetCurrentScale(curve.Axis.Scales.First(s => !s.IsRaw));
        curve.SetCurrentScale(curve.Scales.Single(s => s.IsRaw));
        Assert.Equal(10, curve.Cells[0].RealValue);
        curve.Axis.SetCurrentScale(curve.Axis.Scales.First(s => !s.IsRaw));
        Assert.Equal(1000, curve.Axis.Cells[0].RealValue);
        Assert.Equal(10, curve.Cells[0].RealValue);
    }
}
