using RaiderLite.Core.Model;
using RaiderLite.Core.Plotting;

namespace RaiderLite.Tests;

public class PlottingTests
{
    public PlottingTests()
    {
        CoreSettings.Current = new CoreSettings();
    }

    private static Rom Load() => TestData.LoadInline(SyntheticDefinitionTests.Xml, SyntheticDefinitionTests.MakeImage());

    [Fact]
    public void OnlyCurvesAndMapsCanBeGraphed()
    {
        var rom = Load();
        Assert.True(GraphData.CanGraph(rom.GetTable("Curve")!));
        Assert.True(GraphData.CanGraph(rom.GetTable("Map")!));
        Assert.False(GraphData.CanGraph(rom.GetTable("Scalar")!));
        Assert.False(GraphData.CanGraph(rom.GetTable("Mode")!));
        Assert.Null(GraphData.From(rom.GetTable("Scalar")!));
    }

    [Fact]
    public void CurveUsesScaledAxisAndValues()
    {
        var curve = (Table2D)Load().GetTable("Curve")!;
        var g = GraphData.From(curve)!;
        Assert.False(g.IsMap);
        Assert.Equal(4, g.SizeX);
        Assert.Equal(1, g.SizeY);
        Assert.Equal([1000.0, 2000, 3000, 4000], g.X.Positions);
        Assert.Equal("RPM (RPM)", g.X.Title);
        Assert.False(g.X.Categorical);
        Assert.Equal([5.0, 10, 15, 20], Enumerable.Range(0, 4).Select(i => g.Values[i, 0]));
        Assert.Equal("%", g.ValueTitle);
        Assert.Null(g.Originals);
        Assert.Same(curve.Cells[2], g.Cells[2, 0]);
    }

    [Fact]
    public void StaticAxisIsCategorical()
    {
        var g = GraphData.From(Load().GetTable("StaticCurve")!)!;
        Assert.True(g.X.Categorical);
        Assert.Equal([0.0, 1, 2], g.X.Positions);
        Assert.Equal(["1st", "2nd", "3rd"], g.X.Labels);
    }

    [Fact]
    public void ChangedValuesCarryOriginalsAndCompareValues()
    {
        var a = Load();
        var b = Load();
        var curve = (Table2D)a.GetTable("Curve")!;
        curve.Cells[0].SetBinValue(50);
        var g = GraphData.From(curve)!;
        Assert.NotNull(g.Originals);
        Assert.Equal(25, g.Values[0, 0]);
        Assert.Equal(5, g.Originals![0, 0]);

        curve.SetCompareTable(b.GetTable("Curve"));
        g = GraphData.From(curve)!;
        Assert.NotNull(g.Compared);
        Assert.Equal(5, g.Compared![0, 0]);
        Assert.Equal((5.0, 25.0), g.ValueRange);
    }

    [Fact]
    public void MapAndTranspose()
    {
        var map = (Table3D)Load().GetTable("Map")!;
        var g = GraphData.From(map)!;
        Assert.True(g.IsMap);
        Assert.Equal(3, g.SizeX);
        Assert.Equal(2, g.SizeY);
        Assert.Equal(5, g.Values[2, 1]);
        Assert.Equal([10.0, 20], g.Y!.Positions);

        var t = g.Transposed();
        Assert.Equal(2, t.SizeX);
        Assert.Equal(3, t.SizeY);
        Assert.Equal(5, t.Values[1, 2]);
        Assert.Same(g.Cells[2, 1], t.Cells[1, 2]);
        Assert.Equal(g.Y.Title, t.X.Title);
    }

    [Theory]
    [InlineData(0, 100, 0, 100, 20)]
    [InlineData(0.13, 0.87, 0, 1, 0.2)]
    [InlineData(-40, 120, -50, 150, 50)]
    [InlineData(500, 7000, 0, 8000, 2000)]
    public void NiceTicks(double min, double max, double lo, double hi, double step)
    {
        var t = AxisTicks.Compute(min, max);
        Assert.Equal(lo, t.Min, 9);
        Assert.Equal(hi, t.Max, 9);
        Assert.Equal(step, t.Step, 9);
        Assert.True(t.Values.Count <= 10);
        Assert.Equal(t.Min, t.Values[0], 9);
        Assert.Equal(t.Max, t.Values[^1], 9);
    }

    [Fact]
    public void TicksHandleFlatAndBrokenRanges()
    {
        var flat = AxisTicks.Compute(5, 5);
        Assert.True(flat.Min < 5 && flat.Max > 5);
        var zero = AxisTicks.Compute(0, 0);
        Assert.True(zero.Min < 0 && zero.Max > 0);
        var nan = AxisTicks.Compute(double.NaN, 3);
        Assert.True(nan.Values.Count >= 2);
        Assert.Equal("0", AxisTicks.FormatFor(20));
        Assert.Equal("0.0", AxisTicks.FormatFor(0.5));
        Assert.Equal("0.00", AxisTicks.FormatFor(0.05));
    }

    [Fact]
    public void SurfacePaintsBackToFront()
    {
        var cam = new SurfaceCamera();
        var order = cam.PaintOrder(4, 4, (_, _) => 0.5);
        Assert.Equal(9, order.Count);
        // Rows run away from the viewer, so the last row's quads are painted first.
        var depths = order.Select(q => cam.Project((q.I + 0.5) / 3, (q.J + 0.5) / 3, 0.5).Z).ToList();
        Assert.Equal(depths.OrderBy(d => d), depths);
        Assert.True(cam.Project(0.5, 0, 0.5).Z > cam.Project(0.5, 1, 0.5).Z);
    }

    [Fact]
    public void CameraPitchIsClamped()
    {
        var cam = new SurfaceCamera { Pitch = 200 };
        Assert.Equal(89, cam.Pitch);
        cam.Pitch = -10;
        Assert.Equal(5, cam.Pitch);
        cam.Reset();
        Assert.Equal(SurfaceCamera.DefaultPitch, cam.Pitch);
    }
}
