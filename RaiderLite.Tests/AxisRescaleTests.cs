using System.Text;
using RaiderLite.Core.Model;

namespace RaiderLite.Tests;

/// <summary>
/// Axis rescaling: the new axis and data are stored exactly as planned, nothing else changes, it
/// undoes as one step, and interpolated lookups of the rescaled table match the original.
/// </summary>
public class AxisRescaleTests
{
    public AxisRescaleTests()
    {
        CoreSettings.Current = new CoreSettings();
    }

    private const string Xml = """
        <roms>
          <rom>
            <romid>
              <xmlid>RESCALE</xmlid>
              <internalidaddress>0x0</internalidaddress>
              <internalidstring>RESCALE1</internalidstring>
              <filesize>1024b</filesize>
            </romid>
            <table name="Curve" type="2D" storageaddress="0x20" storagetype="uint8" sizex="8">
              <scaling units="%" expression="x/2" to_byte="x*2" format="0.0"/>
              <table name="RPM" type="X Axis" storageaddress="0x30" storagetype="uint8">
                <scaling units="rpm" expression="x*25" to_byte="x/25" format="0"/>
              </table>
            </table>
            <table name="SharesAxis" type="2D" storageaddress="0x40" storagetype="uint8" sizex="8">
              <table name="RPM" type="X Axis" storageaddress="0x30" storagetype="uint8"/>
            </table>
            <table name="FloatCurve" type="2D" storageaddress="0x100" storagetype="float" sizex="10">
              <table name="X" type="X Axis" storageaddress="0x140" storagetype="float"/>
            </table>
            <table name="Map" type="3D" storageaddress="0x200" storagetype="uint16" sizex="6" sizey="5">
              <scaling units="ms" expression="x/100" to_byte="x*100" format="0.00"/>
              <table name="RPM" type="X Axis" storageaddress="0x260" storagetype="uint16"/>
              <table name="Load" type="Y Axis" storageaddress="0x270" storagetype="uint8">
                <scaling units="kPa" expression="x*2" to_byte="x/2" format="0"/>
              </table>
            </table>
            <table name="SwappedMap" type="3D" storageaddress="0x280" storagetype="uint16" sizex="6" sizey="5" swapxy="true" flipy="true">
              <table name="RPM" type="X Axis" storageaddress="0x2E0" storagetype="uint16"/>
              <table name="Load" type="Y Axis" storageaddress="0x2F0" storagetype="uint8"/>
            </table>
            <table name="Descending" type="2D" storageaddress="0x300" storagetype="uint8" sizex="6">
              <table name="Temp" type="X Axis" storageaddress="0x310" storagetype="uint8"/>
            </table>
            <table name="Labels" type="2D" storageaddress="0x320" storagetype="uint8" sizex="3">
              <table name="gear" type="Static X Axis" sizex="3"><data>1st</data><data>2nd</data><data>3rd</data></table>
            </table>
            <table name="Bumpy" type="2D" storageaddress="0x330" storagetype="uint8" sizex="4">
              <table name="X" type="X Axis" storageaddress="0x338" storagetype="uint8"/>
            </table>
            <table name="Flat" type="2D" storageaddress="0x340" storagetype="uint8" sizex="4">
              <table name="X" type="X Axis" storageaddress="0x348" storagetype="uint8"/>
            </table>
            <table name="Padded" type="2D" storageaddress="0x350" storagetype="uint8" sizex="8">
              <table name="X" type="X Axis" storageaddress="0x358" storagetype="uint8"/>
            </table>
            <table name="Stepped" type="2D" storageaddress="0x360" storagetype="uint8" sizex="6">
              <table name="X" type="X Axis" storageaddress="0x368" storagetype="uint8"/>
            </table>
          </rom>
        </roms>
        """;

    private static readonly double[] MapX = [800, 1500, 2500, 3500, 5000, 6500];
    private static readonly double[] MapY = [10, 25, 40, 60, 80];

    private static readonly double[] PaddedAxis = [10, 10, 10, 20, 30, 40, 50, 60];
    private static readonly double[] PaddedValues = [5, 7, 9, 20, 40, 60, 80, 100];
    private static readonly double[] SteppedAxis = [0, 10, 20, 20, 30, 40];
    private static readonly double[] SteppedValues = [0, 10, 20, 80, 90, 100];

    private static double MapValue(double x, double y) => Math.Round(100 * (1 + x / 2000 + y / 50 + x / 3000 * (y / 40)));

    /// <summary>A rom with every test table filled (through the cells, so any storage layout works), reloaded clean.</summary>
    private static Rom Load()
    {
        var image = new byte[1024];
        Encoding.ASCII.GetBytes("RESCALE1").CopyTo(image, 0);
        var rom = TestData.LoadInline(Xml, image);

        Set(((Table2D)rom.GetTable("Curve")!).Axis.Cells, [20, 40, 60, 80, 100, 120, 140, 160]);
        Set(rom.GetTable("Curve")!.Cells, [10, 30, 60, 90, 110, 120, 125, 128]);
        Set(rom.GetTable("SharesAxis")!.Cells, [1, 2, 3, 4, 5, 6, 7, 8]);
        var floatCurve = (Table2D)rom.GetTable("FloatCurve")!;
        Set(floatCurve.Axis.Cells, Enumerable.Range(0, 10).Select(i => i * 100.0).ToArray());
        Set(floatCurve.Cells, Enumerable.Range(0, 10).Select(i => 50 + 30 * Math.Sin(i * 100.0 / 300)).ToArray());
        foreach (var name in new[] { "Map", "SwappedMap" })
        {
            var map = (Table3D)rom.GetTable(name)!;
            Set(map.XAxis.Cells, MapX);
            Set(map.YAxis.Cells, MapY);
            for (int x = 0; x < MapX.Length; x++)
                for (int y = 0; y < MapY.Length; y++)
                    map.Data3D[x, y].SetBinValue(MapValue(MapX[x], MapY[y]));
        }
        var descending = (Table2D)rom.GetTable("Descending")!;
        Set(descending.Axis.Cells, [200, 160, 120, 80, 40, 0]);
        Set(descending.Cells, [10, 20, 40, 80, 120, 150]);
        Set(rom.GetTable("Labels")!.Cells, [1, 2, 3]);
        Set(((Table2D)rom.GetTable("Bumpy")!).Axis.Cells, [10, 30, 20, 40]);
        Set(rom.GetTable("Bumpy")!.Cells, [1, 2, 3, 4]);
        Set(((Table2D)rom.GetTable("Flat")!).Axis.Cells, [7, 7, 7, 7]);
        // Leading breakpoints padded with repeats, as many real calibrations do.
        Set(((Table2D)rom.GetTable("Padded")!).Axis.Cells, PaddedAxis);
        Set(rom.GetTable("Padded")!.Cells, PaddedValues);
        // A repeated breakpoint inside the axis: the lookup steps from 20 to 80 at 20.
        Set(((Table2D)rom.GetTable("Stepped")!).Axis.Cells, SteppedAxis);
        Set(rom.GetTable("Stepped")!.Cells, SteppedValues);

        return TestData.LoadInline(Xml, (byte[])rom.Binary.Clone());
    }

    private static void Set(DataCell[] cells, double[] values)
    {
        for (int i = 0; i < values.Length; i++)
            cells[i].SetBinValue(values[i]);
    }

    private static double[] Bins(IEnumerable<DataCell> cells) => cells.Select(c => c.BinValue).ToArray();

    private static double[] Reals(IEnumerable<DataCell> cells) => cells.Select(c => c.RealValue).ToArray();

    /// <summary>
    /// Reference ECU lookup, written independently of AxisRescaler.Lookup. Breakpoints are compared
    /// in the axis's own direction (u = x for an increasing axis, -x for a decreasing one). Below the
    /// first breakpoint: the first value. Otherwise: the segment starting at the LAST breakpoint at
    /// or below u, so at a repeated breakpoint the lookup has already jumped to the last repeat.
    /// </summary>
    private static double RefLookup(IReadOnlyList<double> axis, IReadOnlyList<double> values, double x)
    {
        double s = axis[^1] >= axis[0] ? 1 : -1;
        var u = axis.Select(a => a * s).ToArray();
        double ux = x * s;
        if (ux < u[0])
            return values[0];
        int k = Array.FindLastIndex(u, a => a <= ux);
        if (k == u.Length - 1)
            return values[k];
        return values[k] + (values[k + 1] - values[k]) * (ux - u[k]) / (u[k + 1] - u[k]);
    }

    /// <summary>
    /// Reference for the value just before x (limit from the axis's first end): the segment ending at
    /// the FIRST breakpoint at or above u, so a repeated breakpoint's first value is reached.
    /// </summary>
    private static double RefLookupFromBefore(IReadOnlyList<double> axis, IReadOnlyList<double> values, double x)
    {
        double s = axis[^1] >= axis[0] ? 1 : -1;
        var u = axis.Select(a => a * s).ToArray();
        double ux = x * s;
        if (ux <= u[0])
            return values[0];
        int k = Array.FindIndex(u, a => a >= ux);
        if (k < 0)
            return values[^1];
        return values[k - 1] + (values[k] - values[k - 1]) * (ux - u[k - 1]) / (u[k] - u[k - 1]);
    }

    /// <summary>
    /// Largest |new - old| lookup difference by brute force over the old axis range: 200 points
    /// inside every segment, plus both sides of every breakpoint (where repeated breakpoints jump).
    /// </summary>
    private static double BruteForceMaxChange(double[] oldAxis, double[] oldValues, double[] newAxis, double[] newValues, Func<double, double> toReal)
    {
        double lo = Math.Min(oldAxis[0], oldAxis[^1]), hi = Math.Max(oldAxis[0], oldAxis[^1]);
        double start = oldAxis[^1] >= oldAxis[0] ? lo : hi;
        var xs = oldAxis.Concat(newAxis).Where(x => x >= lo && x <= hi).Append(lo).Append(hi).Distinct().Order().ToList();
        double Change(double x) => Math.Abs(toReal(RefLookup(newAxis, newValues, x)) - toReal(RefLookup(oldAxis, oldValues, x)));
        double ChangeBefore(double x) => Math.Abs(toReal(RefLookupFromBefore(newAxis, newValues, x)) - toReal(RefLookupFromBefore(oldAxis, oldValues, x)));
        double worst = xs.Max(x => Math.Max(Change(x), x == start ? 0 : ChangeBefore(x)));
        for (int i = 0; i + 1 < xs.Count; i++)
            for (int k = 1; k < 200; k++)
                worst = Math.Max(worst, Change(xs[i] + (xs[i + 1] - xs[i]) * k / 200));
        return worst;
    }

    /// <summary>
    /// What each rescaled value must be, from the reference lookups: the last of a run of equal new
    /// breakpoints (or a lone one) is the old lookup there; the first of a run is the old value just
    /// before it; ones in between are unused and keep their old value if the old axis repeats there too.
    /// </summary>
    private static double[] ExpectedResample(double[] oldAxis, double[] oldValues, double[] newAxis)
    {
        var expected = new double[newAxis.Length];
        for (int i = 0; i < newAxis.Length; i++)
        {
            int first = i, last = i;
            while (first > 0 && newAxis[first - 1] == newAxis[i])
                first--;
            while (last + 1 < newAxis.Length && newAxis[last + 1] == newAxis[i])
                last++;
            bool oldRepeats = Enumerable.Range(first, last - first + 1).All(k => oldAxis[k] == oldAxis[first]);
            expected[i] = i == last ? RefLookup(oldAxis, oldValues, newAxis[i])
                : i == first ? RefLookupFromBefore(oldAxis, oldValues, newAxis[i])
                : oldRepeats ? oldValues[i] : RefLookup(oldAxis, oldValues, newAxis[i]);
        }
        return expected;
    }

    // ===== lookup and resampling maths =====

    [Fact]
    public void LookupIsExactAtBreakpointsLinearBetweenAndClampedOutside()
    {
        double[] axis = [0, 10, 30];
        double[] values = [5, 25, 15];
        Assert.Equal(5, AxisRescaler.Lookup(axis, values, -100));
        Assert.Equal(5, AxisRescaler.Lookup(axis, values, 0));
        Assert.Equal(15, AxisRescaler.Lookup(axis, values, 5));
        Assert.Equal(25, AxisRescaler.Lookup(axis, values, 10));
        Assert.Equal(20, AxisRescaler.Lookup(axis, values, 20));
        Assert.Equal(15, AxisRescaler.Lookup(axis, values, 30));
        Assert.Equal(15, AxisRescaler.Lookup(axis, values, 1000));

        // A decreasing axis is the same curve read the other way.
        double[] down = [30, 10, 0];
        double[] downValues = [15, 25, 5];
        foreach (var x in new[] { -5.0, 0, 3, 10, 17, 30, 40 })
            Assert.Equal(AxisRescaler.Lookup(axis, values, x), AxisRescaler.Lookup(down, downValues, x), 12);
    }

    [Fact]
    public void LookupMatchesTheReferenceOnRandomTables()
    {
        var rng = new Random(1);
        for (int trial = 0; trial < 500; trial++)
        {
            var (axis, values) = RandomTable(rng, rng.Next(2, 20), descending: trial % 3 == 0);
            double lo = axis.Min() - 50, hi = axis.Max() + 50;
            for (int k = 0; k < 50; k++)
            {
                double x = lo + rng.NextDouble() * (hi - lo);
                Assert.Equal(RefLookup(axis, values, x), AxisRescaler.Lookup(axis, values, x), 9);
            }
        }
    }

    [Fact]
    public void ResamplingOntoTheSameAxisChangesNothing()
    {
        var rng = new Random(2);
        for (int trial = 0; trial < 200; trial++)
        {
            var (axis, values) = RandomTable(rng, rng.Next(2, 20), descending: trial % 2 == 0);
            Assert.Equal(values, AxisRescaler.Resample(axis, values, axis, BeyondRange.Hold));
            Assert.Equal(values, AxisRescaler.Resample(axis, values, axis, BeyondRange.Extrapolate));
        }
    }

    [Fact]
    public void ResampledValuesAreTheOldLookupAtTheNewBreakpoints()
    {
        var rng = new Random(3);
        for (int trial = 0; trial < 500; trial++)
        {
            var (axis, values) = RandomTable(rng, rng.Next(2, 20), descending: false);
            var (newAxis, _) = RandomTable(rng, rng.Next(2, 20), descending: false);
            var resampled = AxisRescaler.Resample(axis, values, newAxis, BeyondRange.Hold);
            for (int i = 0; i < newAxis.Length; i++)
                Assert.Equal(RefLookup(axis, values, newAxis[i]), resampled[i], 9);
        }
    }

    [Fact]
    public void LinearDataIsReproducedExactlyAnywhereInRange()
    {
        // A straight line is reproduced exactly by any breakpoints: rescaling can't change it.
        var rng = new Random(4);
        for (int trial = 0; trial < 200; trial++)
        {
            double slope = rng.NextDouble() * 4 - 2, offset = rng.NextDouble() * 100;
            var (axis, _) = RandomTable(rng, rng.Next(2, 16), descending: false);
            var values = axis.Select(x => slope * x + offset).ToArray();
            var newAxis = AxisRescaler.Even(axis[0], axis[^1], rng.Next(2, 16));
            var resampled = AxisRescaler.Resample(axis, values, newAxis, BeyondRange.Hold);
            for (int k = 0; k < 50; k++)
            {
                double x = axis[0] + rng.NextDouble() * (axis[^1] - axis[0]);
                Assert.Equal(slope * x + offset, AxisRescaler.Lookup(newAxis, resampled, x), 6);
            }
        }
    }

    [Fact]
    public void HoldKeepsTheEdgeValuesAndExtrapolateContinuesTheSlope()
    {
        double[] axis = [10, 20, 30];
        double[] values = [100, 120, 150];
        double[] newAxis = [0, 10, 25, 40, 50];
        Assert.Equal([100.0, 100, 135, 150, 150], AxisRescaler.Resample(axis, values, newAxis, BeyondRange.Hold));
        // First segment slope 2/unit, last 3/unit.
        Assert.Equal([80.0, 100, 135, 180, 210], AxisRescaler.Resample(axis, values, newAxis, BeyondRange.Extrapolate));

        // Decreasing axis: "before the first breakpoint" is above it.
        double[] down = [30, 20, 10];
        double[] downValues = [150, 120, 100];
        Assert.Equal([210.0, 135, 80], AxisRescaler.Resample(down, downValues, [50, 25, 0], BeyondRange.Extrapolate));
        Assert.Equal([150.0, 135, 100], AxisRescaler.Resample(down, downValues, [50, 25, 0], BeyondRange.Hold));
    }

    [Fact]
    public void StretchKeepsTheFirstBreakpointAndTheSpacing()
    {
        double[] axis = [500, 750, 1000, 1500, 2500, 4000];
        var stretched = AxisRescaler.Stretch(axis, 7500);
        Assert.Equal(500, stretched[0]);
        Assert.Equal(7500, stretched[^1]);
        for (int i = 1; i < axis.Length; i++)
        {
            // Each gap grows by the same factor (7000 / 3500).
            Assert.Equal(2 * (axis[i] - axis[i - 1]), stretched[i] - stretched[i - 1], 9);
        }

        double[] down = [100, 80, 40, 0];
        Assert.Equal([100.0, 70, 10, -50], AxisRescaler.Stretch(down, -50));
    }

    [Fact]
    public void EvenSpacingHitsBothEnds()
    {
        Assert.Equal([0.0, 2.5, 5, 7.5, 10], AxisRescaler.Even(0, 10, 5));
        Assert.Equal([300.0, 200, 100], AxisRescaler.Even(300, 100, 3));
    }

    [Fact]
    public void ReportedMaxChangeIsTheTrueMaximum()
    {
        // The difference of two piecewise-linear lookups peaks at a breakpoint, so checking the
        // breakpoints must find the same maximum as dense sampling.
        var rng = new Random(5);
        for (int trial = 0; trial < 300; trial++)
        {
            var (axis, values) = RandomTable(rng, rng.Next(2, 16), descending: trial % 4 == 0);
            var newAxis = AxisRescaler.Stretch(axis, axis[^1] + (axis[^1] - axis[0]) * (rng.NextDouble() * 1.5 - 0.4));
            if (AxisRescaler.Direction(newAxis) != AxisRescaler.Direction(axis))
                continue;
            var resampled = AxisRescaler.Resample(axis, values, newAxis, trial % 2 == 0 ? BeyondRange.Hold : BeyondRange.Extrapolate);
            double lo = Math.Min(axis[0], axis[^1]), hi = Math.Max(axis[0], axis[^1]);
            var (reported, at) = AxisRescaler.MaxDifference(axis, values, newAxis, resampled, lo, hi);
            double brute = BruteForceMaxChange(axis, values, newAxis, resampled, v => v);
            Assert.Equal(brute, Math.Abs(reported), 9);
            Assert.Equal(Math.Abs(reported), Math.Abs(RefLookup(newAxis, resampled, at) - RefLookup(axis, values, at)), 9);
        }
    }

    // ===== rescaled smooth curves look up approximately the same =====

    public static TheoryData<string> SmoothFunctions => new() { "sine", "parabola", "logistic" };

    /// <summary>The function and a bound on |f''| over 500..6500.</summary>
    private static (Func<double, double> F, double MaxSecondDerivative) Smooth(string name) => name switch
    {
        "sine" => (x => 40 + 25 * Math.Sin(x / 700), 25 / (700.0 * 700)),
        "parabola" => (x => 10 + 0.002 * (x - 3000) * (x - 3000), 0.004),
        _ => (x => 100 / (1 + Math.Exp(-(x - 3500) / 600)), 100 * 0.0963 / (600.0 * 600)),
    };

    [Theory]
    [MemberData(nameof(SmoothFunctions))]
    public void ExpandedSmoothCurveLooksUpApproximatelyTheSame(string function)
    {
        // A 16-point table of a smooth curve, denser at the low end, expanded from 6500 to 9000.
        var (f, m2) = Smooth(function);
        var oldAxis = Enumerable.Range(0, 16).Select(i => 500 + 6000 * Math.Pow(i / 15.0, 1.5)).ToArray();
        var oldValues = oldAxis.Select(f).ToArray();
        var newAxis = AxisRescaler.Stretch(oldAxis, 9000);
        var newValues = AxisRescaler.Resample(oldAxis, oldValues, newAxis, BeyondRange.Hold);

        double oldMax = oldAxis[^1];
        double hOld = oldAxis.Zip(oldAxis.Skip(1)).Max(p => p.Second - p.First);
        double hNew = newAxis.Zip(newAxis.Skip(1)).Max(p => p.Second - p.First);
        int last = Array.FindLastIndex(newAxis, x => x <= oldMax);

        // Where the new breakpoints lie inside the old range, both tables approximate the same
        // smooth curve: linear interpolation error is at most h²·max|f''|/8 for each table, so
        // |new - old| <= (2·hOld² + hNew²)·max|f''| / 8.
        double bound = (2 * hOld * hOld + hNew * hNew) * m2 / 8;
        var rng = new Random(6);
        for (int k = 0; k < 10000; k++)
        {
            double x = oldAxis[0] + rng.NextDouble() * (newAxis[last] - oldAxis[0]);
            double change = Math.Abs(AxisRescaler.Lookup(newAxis, newValues, x) - AxisRescaler.Lookup(oldAxis, oldValues, x));
            Assert.True(change <= bound + 1e-9, $"{function}: change {change} at {x} exceeds {bound}");
        }

        // In the one segment that straddles the old end, the new table ramps to the held value:
        // it stays within the range the old curve covers there.
        double segLo = newAxis[last], segHi = newAxis[last + 1];
        double minOld = Enumerable.Range(0, 1001).Select(k => AxisRescaler.Lookup(oldAxis, oldValues, segLo + (oldMax - segLo) * k / 1000.0)).Min();
        double maxOld = Enumerable.Range(0, 1001).Select(k => AxisRescaler.Lookup(oldAxis, oldValues, segLo + (oldMax - segLo) * k / 1000.0)).Max();
        for (int k = 0; k <= 1000; k++)
        {
            double x = segLo + (segHi - segLo) * k / 1000.0;
            double change = Math.Abs(AxisRescaler.Lookup(newAxis, newValues, x) - AxisRescaler.Lookup(oldAxis, oldValues, x));
            Assert.True(change <= maxOld - minOld + 1e-9, $"{function}: change {change} at {x} in the straddling segment");
        }

        // Beyond that, held values reproduce the ECU's clamped lookup exactly.
        for (double x = segHi; x <= 9500; x += 37)
            Assert.Equal(AxisRescaler.Lookup(oldAxis, oldValues, x), AxisRescaler.Lookup(newAxis, newValues, x), 9);
    }

    [Theory]
    [MemberData(nameof(SmoothFunctions))]
    public void RespacedSmoothCurveLooksUpApproximatelyTheSame(string function)
    {
        // Same range, evenly respaced breakpoints: the second-order bound holds everywhere.
        var (f, m2) = Smooth(function);
        var oldAxis = Enumerable.Range(0, 16).Select(i => 500 + 6000 * Math.Pow(i / 15.0, 1.5)).ToArray();
        var oldValues = oldAxis.Select(f).ToArray();
        var newAxis = AxisRescaler.Even(500, 6500, 16);
        var newValues = AxisRescaler.Resample(oldAxis, oldValues, newAxis, BeyondRange.Hold);
        double hOld = oldAxis.Zip(oldAxis.Skip(1)).Max(p => p.Second - p.First);
        double hNew = newAxis.Zip(newAxis.Skip(1)).Max(p => p.Second - p.First);
        double bound = (2 * hOld * hOld + hNew * hNew) * m2 / 8;
        double brute = BruteForceMaxChange(oldAxis, oldValues, newAxis, newValues, v => v);
        Assert.True(brute <= bound, $"{function}: max change {brute} exceeds {bound}");
    }

    // ===== rescaling real tables in an image =====

    [Fact]
    public void StretchingACurveStoresExactlyThePlannedValues()
    {
        var rom = Load();
        var curve = (Table2D)rom.GetTable("Curve")!;
        var oldAxis = Bins(curve.Axis.Cells);
        var oldData = Bins(curve.Cells);
        var before = (byte[])rom.Binary.Clone();

        // 500..4000 rpm stretched to 500..6000 rpm.
        var newReal = AxisRescaler.Stretch(Reals(curve.Axis.Cells), 6000);
        var plan = AxisRescaler.Plan(curve, curve.Axis, newReal, BeyondRange.Hold);
        Assert.True(plan.CanApply, plan.Error);
        plan.Apply();

        // Axis: each real breakpoint converted with to_byte (x/25) and rounded.
        var expectedAxis = newReal.Select(r => Math.Round(r / 25, MidpointRounding.AwayFromZero)).ToArray();
        Assert.Equal([20.0, 51, 83, 114, 146, 177, 209, 240], expectedAxis);
        Assert.Equal(expectedAxis, Bins(curve.Axis.Cells));
        // Data: the old lookup at each stored breakpoint, rounded to a whole raw value.
        var expectedData = expectedAxis.Select(x => Math.Round(RefLookup(oldAxis, oldData, x), MidpointRounding.AwayFromZero)).ToArray();
        Assert.Equal(expectedData, Bins(curve.Cells));
        Assert.Equal(plan.NewAxis, Bins(curve.Axis.Cells));
        Assert.Equal(plan.NewValues[0], Bins(curve.Cells));

        // The bytes hold exactly those values, and nothing else in the image changed.
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(expectedAxis[i], rom.Binary[0x30 + i]);
            Assert.Equal(expectedData[i], rom.Binary[0x20 + i]);
        }
        for (int i = 0; i < before.Length; i++)
        {
            if (i is not (>= 0x20 and < 0x28 or >= 0x30 and < 0x38))
                Assert.True(before[i] == rom.Binary[i], $"byte 0x{i:X} changed");
        }

        // Reading the image back gives the same table.
        var reloaded = (Table2D)TestData.LoadInline(Xml, (byte[])rom.Binary.Clone()).GetTable("Curve")!;
        Assert.Equal(expectedAxis, Bins(reloaded.Axis.Cells));
        Assert.Equal(expectedData, Bins(reloaded.Cells));

        // Beyond 4000 rpm the held last value; the reported max change is the real one.
        Assert.Equal(oldData[^1], curve.Cells[^1].BinValue);
        double brute = BruteForceMaxChange(oldAxis, oldData, Bins(curve.Axis.Cells), Bins(curve.Cells), curve.CurrentScale.ToReal);
        Assert.Equal(brute, Math.Abs(plan.MaxChange), 9);
    }

    [Fact]
    public void RescaleIsOneUndoStepAndRedoReappliesIt()
    {
        var rom = Load();
        var curve = (Table2D)rom.GetTable("Curve")!;
        var original = (byte[])rom.Binary.Clone();
        AxisRescaler.Plan(curve, curve.Axis, AxisRescaler.Stretch(Reals(curve.Axis.Cells), 6000), BeyondRange.Hold).Apply();
        var rescaled = (byte[])rom.Binary.Clone();

        Assert.Equal("Rescale axis", rom.History.UndoName);
        rom.History.Undo();
        Assert.False(rom.History.CanUndo);
        Assert.Equal(original, rom.Binary);
        Assert.Equal([20.0, 40, 60, 80, 100, 120, 140, 160], Bins(curve.Axis.Cells));
        rom.History.Redo();
        Assert.Equal(rescaled, rom.Binary);
    }

    [Fact]
    public void MapXAxisRescaleResamplesEachRowAndLeavesTheYAxisAlone()
    {
        foreach (var name in new[] { "Map", "SwappedMap" })
        {
            var rom = Load();
            var map = (Table3D)rom.GetTable(name)!;
            var yBefore = Bins(map.YAxis.Cells);
            var plan = AxisRescaler.Plan(map, map.XAxis, AxisRescaler.Stretch(Reals(map.XAxis.Cells), 9000), BeyondRange.Hold);
            Assert.True(plan.CanApply, plan.Error);
            plan.Apply();

            var newX = Bins(map.XAxis.Cells);
            // Gaps grow by 8200 / 5700.
            Assert.Equal([800.0, 1807, 3246, 4684, 6842, 9000], newX);
            Assert.Equal(yBefore, Bins(map.YAxis.Cells));
            for (int y = 0; y < MapY.Length; y++)
            {
                var oldRow = MapX.Select(x => MapValue(x, MapY[y])).ToArray();
                for (int x = 0; x < newX.Length; x++)
                {
                    double expected = Math.Round(RefLookup(MapX, oldRow, newX[x]), MidpointRounding.AwayFromZero);
                    Assert.Equal(expected, map.Data3D[x, y].BinValue);
                }
            }

            // The storage layout (swapped and flipped for SwappedMap) round-trips through the bytes.
            var reloaded = (Table3D)TestData.LoadInline(Xml, (byte[])rom.Binary.Clone()).GetTable(name)!;
            Assert.Equal(Bins(map.DataCells), Bins(reloaded.DataCells));
            Assert.Equal(newX, Bins(reloaded.XAxis.Cells));
        }
    }

    [Fact]
    public void MapYAxisRescaleResamplesEachColumn()
    {
        var rom = Load();
        var map = (Table3D)rom.GetTable("Map")!;
        var xBefore = Bins(map.XAxis.Cells);
        // 20..160 kPa evenly respaced over 20..200 kPa (raw 10..100).
        var plan = AxisRescaler.Plan(map, map.YAxis, AxisRescaler.Even(20, 200, 5), BeyondRange.Hold);
        Assert.True(plan.CanApply, plan.Error);
        plan.Apply();

        var newY = Bins(map.YAxis.Cells);
        Assert.Equal([10.0, 33, 55, 78, 100], newY);
        Assert.Equal(xBefore, Bins(map.XAxis.Cells));
        for (int x = 0; x < MapX.Length; x++)
        {
            var oldColumn = MapY.Select(y => MapValue(MapX[x], y)).ToArray();
            for (int y = 0; y < newY.Length; y++)
                Assert.Equal(Math.Round(RefLookup(MapY, oldColumn, newY[y]), MidpointRounding.AwayFromZero), map.Data3D[x, y].BinValue);
        }
    }

    [Fact]
    public void FloatTablesResampleWithoutRounding()
    {
        var rom = Load();
        var curve = (Table2D)rom.GetTable("FloatCurve")!;
        var oldAxis = Bins(curve.Axis.Cells);
        var oldData = Bins(curve.Cells);
        var plan = AxisRescaler.Plan(curve, curve.Axis, AxisRescaler.Stretch(oldAxis, 1250), BeyondRange.Hold);
        Assert.True(plan.CanApply, plan.Error);
        plan.Apply();

        var newAxis = Bins(curve.Axis.Cells);
        for (int i = 0; i < newAxis.Length; i++)
        {
            Assert.Equal((float)(i * 1250.0 / 9), newAxis[i]);
            Assert.Equal((float)RefLookup(oldAxis, oldData, newAxis[i]), curve.Cells[i].BinValue);
        }
    }

    [Fact]
    public void ExtrapolatingALinearCurveContinuesItExactly()
    {
        var rom = Load();
        var curve = (Table2D)rom.GetTable("FloatCurve")!;
        Set(curve.Cells, Bins(curve.Axis.Cells).Select(x => 2 + 0.5 * x).ToArray());
        AxisRescaler.Plan(curve, curve.Axis, AxisRescaler.Stretch(Bins(curve.Axis.Cells), 1800), BeyondRange.Extrapolate).Apply();
        var newAxis = Bins(curve.Axis.Cells);
        for (double x = 0; x <= 1800; x += 25)
            Assert.Equal(2 + 0.5 * x, AxisRescaler.Lookup(newAxis, Bins(curve.Cells), x), 3);
    }

    [Fact]
    public void DescendingAxesRescaleInTheirOwnDirection()
    {
        var rom = Load();
        var table = (Table2D)rom.GetTable("Descending")!;
        double[] oldAxis = [200, 160, 120, 80, 40, 0];
        double[] oldData = [10, 20, 40, 80, 120, 150];
        var plan = AxisRescaler.Plan(table, table.Axis, [250, 200, 150, 100, 50, 0], BeyondRange.Hold);
        Assert.True(plan.CanApply, plan.Error);
        plan.Apply();
        Assert.Equal([250.0, 200, 150, 100, 50, 0], Bins(table.Axis.Cells));
        // 250 is beyond the old top (200): it holds that end's value.
        Assert.Equal([10.0, 10, 25, 60, 110, 150], Bins(table.Cells));
        for (int i = 0; i < 6; i++)
            Assert.Equal(Math.Round(RefLookup(oldAxis, oldData, table.Axis.Cells[i].BinValue), MidpointRounding.AwayFromZero), table.Cells[i].BinValue);

        var reversed = AxisRescaler.Plan(table, table.Axis, [0, 50, 100, 150, 200, 250], BeyondRange.Hold);
        Assert.Contains("opposite direction", reversed.Error);
    }

    [Fact]
    public void ExtrapolatedValuesAreLimitedToTheStorageRange()
    {
        var rom = Load();
        var curve = (Table2D)rom.GetTable("Curve")!;
        // The last segment rises 3 raw per 20 raw of axis; far beyond, the value would pass 255.
        Set(curve.Cells, [10, 30, 60, 90, 120, 160, 200, 240]);
        var plan = AxisRescaler.Plan(curve, curve.Axis, AxisRescaler.Stretch(Reals(curve.Axis.Cells), 6375), BeyondRange.Extrapolate);
        Assert.True(plan.CanApply, plan.Error);
        Assert.True(plan.LimitedValues > 0);
        Assert.Contains(plan.Warnings, w => w.Contains("limited", StringComparison.Ordinal));
        plan.Apply();
        Assert.Equal(255, curve.Cells[^1].BinValue);
    }

    [Fact]
    public void WarnsAboutTablesSharingTheBytes()
    {
        var rom = Load();
        var curve = (Table2D)rom.GetTable("Curve")!;
        var plan = AxisRescaler.Plan(curve, curve.Axis, AxisRescaler.Stretch(Reals(curve.Axis.Cells), 6000), BeyondRange.Hold);
        Assert.Contains(plan.Warnings, w => w.Contains("SharesAxis", StringComparison.Ordinal));

        var floatCurve = (Table2D)rom.GetTable("FloatCurve")!;
        Assert.Empty(AxisRescaler.Plan(floatCurve, floatCurve.Axis, AxisRescaler.Stretch(Reals(floatCurve.Axis.Cells), 1000), BeyondRange.Hold).Warnings);
    }

    [Fact]
    public void WarnsWhenTheNewAxisNoLongerCoversTheOldRange()
    {
        var rom = Load();
        var curve = (Table2D)rom.GetTable("Curve")!;
        var plan = AxisRescaler.Plan(curve, curve.Axis, AxisRescaler.Stretch(Reals(curve.Axis.Cells), 3000), BeyondRange.Hold);
        Assert.True(plan.CanApply, plan.Error);
        Assert.Contains(plan.Warnings, w => w.Contains("doesn't cover", StringComparison.Ordinal));
    }

    [Fact]
    public void RescalingToTheSameAxisWritesNothing()
    {
        var rom = Load();
        var map = (Table3D)rom.GetTable("Map")!;
        var before = (byte[])rom.Binary.Clone();
        var plan = AxisRescaler.Plan(map, map.XAxis, Reals(map.XAxis.Cells), BeyondRange.Hold);
        Assert.Equal(0, plan.MaxChange);
        plan.Apply();
        Assert.Equal(before, rom.Binary);
        Assert.False(rom.History.CanUndo);
    }

    // ===== repeated breakpoints =====

    [Fact]
    public void LookupJumpsAtARepeatedBreakpoint()
    {
        double[] axis = SteppedAxis;
        double[] values = SteppedValues;
        // From 20 on the lookup uses the last repeat (80); just before 20 it approaches 20.
        Assert.Equal(15, AxisRescaler.Lookup(axis, values, 15));
        Assert.Equal(80, AxisRescaler.Lookup(axis, values, 20));
        Assert.Equal(20, AxisRescaler.LookupFromBefore(axis, values, 20));
        Assert.Equal(85, AxisRescaler.Lookup(axis, values, 25));
        Assert.Equal(85, AxisRescaler.LookupFromBefore(axis, values, 25));

        // Leading repeats: below the axis the first value; from 10 on, the last repeat's.
        Assert.Equal(5, AxisRescaler.Lookup(PaddedAxis, PaddedValues, 0));
        Assert.Equal(5, AxisRescaler.LookupFromBefore(PaddedAxis, PaddedValues, 10));
        Assert.Equal(9, AxisRescaler.Lookup(PaddedAxis, PaddedValues, 10));
        Assert.Equal(14.5, AxisRescaler.Lookup(PaddedAxis, PaddedValues, 15));

        // Trailing repeats: the last repeat's value from there on.
        double[] tail = [0, 10, 20, 20];
        double[] tailValues = [0, 10, 30, 50];
        Assert.Equal(20, AxisRescaler.Lookup(tail, tailValues, 15));
        Assert.Equal(50, AxisRescaler.Lookup(tail, tailValues, 20));
        Assert.Equal(30, AxisRescaler.LookupFromBefore(tail, tailValues, 20));
        Assert.Equal(50, AxisRescaler.Lookup(tail, tailValues, 99));
    }

    [Fact]
    public void LookupsWithRepeatsMatchTheReference()
    {
        var rng = new Random(7);
        for (int trial = 0; trial < 500; trial++)
        {
            var (axis, values) = RandomTableWithRepeats(rng, rng.Next(2, 20), descending: trial % 3 == 0);
            if (AxisRescaler.Direction(axis) == 0)
                continue;
            double lo = axis.Min() - 50, hi = axis.Max() + 50;
            foreach (var x in axis.Concat(Enumerable.Range(0, 30).Select(_ => lo + rng.NextDouble() * (hi - lo))))
            {
                Assert.Equal(RefLookup(axis, values, x), AxisRescaler.Lookup(axis, values, x), 9);
                Assert.Equal(RefLookupFromBefore(axis, values, x), AxisRescaler.LookupFromBefore(axis, values, x), 9);
            }
        }
    }

    [Fact]
    public void ResamplingWithRepeatsIsExactAndReportsTheTrueMaximum()
    {
        var rng = new Random(8);
        int checkedCount = 0;
        for (int trial = 0; trial < 500; trial++)
        {
            bool descending = trial % 4 == 0;
            var (axis, values) = RandomTableWithRepeats(rng, rng.Next(2, 16), descending);
            if (AxisRescaler.Direction(axis) == 0)
                continue;
            // Onto the same axis: nothing changes, repeats included.
            Assert.Equal(values, AxisRescaler.Resample(axis, values, axis, BeyondRange.Hold));

            double span = axis[^1] - axis[0];
            var newAxis = trial % 2 == 0
                ? AxisRescaler.Stretch(axis, axis[^1] + span * (rng.NextDouble() - 0.3))
                : AxisRescaler.Even(axis[0], axis[^1] + span * rng.NextDouble(), axis.Length);
            if (AxisRescaler.FirstBadStep(axis, newAxis) > 0)
                continue;
            var resampled = AxisRescaler.Resample(axis, values, newAxis, BeyondRange.Hold);
            var expected = ExpectedResample(axis, values, newAxis);
            for (int i = 0; i < newAxis.Length; i++)
                Assert.Equal(expected[i], resampled[i], 9);

            double lo = Math.Min(axis[0], axis[^1]), hi = Math.Max(axis[0], axis[^1]);
            var (reported, _) = AxisRescaler.MaxDifference(axis, values, newAxis, resampled, lo, hi);
            Assert.Equal(BruteForceMaxChange(axis, values, newAxis, resampled, v => v), Math.Abs(reported), 9);
            checkedCount++;
        }
        Assert.True(checkedCount > 200);
    }

    [Fact]
    public void StretchingAPaddedAxisKeepsThePaddingAsItWas()
    {
        var rom = Load();
        var table = (Table2D)rom.GetTable("Padded")!;
        var plan = AxisRescaler.Plan(table, table.Axis, AxisRescaler.Stretch(PaddedAxis, 110), BeyondRange.Hold);
        Assert.True(plan.CanApply, plan.Error);
        plan.Apply();

        // The repeats stay repeats at 10, with the same values: below 10 the first value, from 10
        // on the third, and the unused one between them untouched.
        Assert.Equal([10.0, 10, 10, 30, 50, 70, 90, 110], Bins(table.Axis.Cells));
        var values = Bins(table.Cells);
        Assert.Equal([5.0, 7, 9], values[..3]);
        Assert.Equal(ExpectedResample(PaddedAxis, PaddedValues, Bins(table.Axis.Cells)).Select(v => Math.Round(v, MidpointRounding.AwayFromZero)), values);
        Assert.Equal(BruteForceMaxChange(PaddedAxis, PaddedValues, Bins(table.Axis.Cells), values, v => v), Math.Abs(plan.MaxChange), 9);
    }

    [Fact]
    public void EvenSpacingMaySpreadOutRepeatedBreakpoints()
    {
        var rom = Load();
        var table = (Table2D)rom.GetTable("Padded")!;
        var plan = AxisRescaler.Plan(table, table.Axis, AxisRescaler.Even(10, 80, 8), BeyondRange.Hold);
        Assert.True(plan.CanApply, plan.Error);
        plan.Apply();
        Assert.Equal([10.0, 20, 30, 40, 50, 60, 70, 80], Bins(table.Axis.Cells));
        // 10 is now a single breakpoint: it takes the lookup from 10 on.
        Assert.Equal(9, table.Cells[0].BinValue);
        Assert.Equal(PaddedValues[^1], table.Cells[^1].BinValue);
    }

    [Fact]
    public void AStepThatMovesWithTheAxisIsMeasuredFromBothSides()
    {
        var rom = Load();
        var table = (Table2D)rom.GetTable("Stepped")!;
        // 0..40 stretched to 0..60: the repeat moves from 20 to 30, so the step at 20 can't be kept.
        var plan = AxisRescaler.Plan(table, table.Axis, AxisRescaler.Stretch(SteppedAxis, 60), BeyondRange.Hold);
        Assert.True(plan.CanApply, plan.Error);
        plan.Apply();
        var newAxis = Bins(table.Axis.Cells);
        Assert.Equal([0.0, 15, 30, 30, 45, 60], newAxis);
        Assert.Equal(ExpectedResample(SteppedAxis, SteppedValues, newAxis).Select(v => Math.Round(v, MidpointRounding.AwayFromZero)), Bins(table.Cells));
        double brute = BruteForceMaxChange(SteppedAxis, SteppedValues, newAxis, Bins(table.Cells), v => v);
        Assert.Equal(brute, Math.Abs(plan.MaxChange), 9);
        // The old lookup jumps 20 -> 80 at 20; the new one passes 20 on its way up from 15 to 90.
        Assert.True(Math.Abs(plan.MaxChange) >= 40);
    }

    [Fact]
    public void ARepeatOnlyMayAppearWhereTheOldAxisRepeats()
    {
        var rom = Load();
        var table = (Table2D)rom.GetTable("Stepped")!;
        Assert.Null(AxisRescaler.Plan(table, table.Axis, [0, 10, 20, 20, 30, 40], BeyondRange.Hold).Error);
        Assert.Null(AxisRescaler.Plan(table, table.Axis, [0, 10, 20, 25, 30, 40], BeyondRange.Hold).Error);
        Assert.Contains("can't separate", AxisRescaler.Plan(table, table.Axis, [0, 10, 10, 20, 30, 40], BeyondRange.Hold).Error);
    }

    // ===== refusals =====

    [Fact]
    public void RefusesWhatCantBeRescaled()
    {
        var rom = Load();
        var curve = (Table2D)rom.GetTable("Curve")!;
        var real = Reals(curve.Axis.Cells);
        var before = (byte[])rom.Binary.Clone();

        string? Error(Table table, Table1D axis, double[] values) => AxisRescaler.Plan(table, axis, values, BeyondRange.Hold).Error;

        var labels = (Table2D)rom.GetTable("Labels")!;
        Assert.Contains("labels", Error(labels, labels.Axis, [1, 2, 3]));

        var bumpy = (Table2D)rom.GetTable("Bumpy")!;
        Assert.Contains("both up and down", Error(bumpy, bumpy.Axis, [10, 20, 30, 40]));
        var flat = (Table2D)rom.GetTable("Flat")!;
        Assert.Contains("all the axis values are the same", Error(flat, flat.Axis, [10, 20, 30, 40]), StringComparison.OrdinalIgnoreCase);

        // uint8 x 25 rpm stores at most 6375 rpm.
        Assert.Contains("outside what the axis can store", Error(curve, curve.Axis, AxisRescaler.Stretch(real, 8000)));
        Assert.Contains("8 breakpoints", Error(curve, curve.Axis, [500, 1000, 2000]));
        Assert.Contains("not a number", Error(curve, curve.Axis, [500, double.NaN, 1500, 2000, 2500, 3000, 3500, 4000]));
        // 25 rpm resolution can't separate 8 breakpoints within 100 rpm.
        Assert.Contains("can't separate", Error(curve, curve.Axis, AxisRescaler.Even(500, 600, 8)));

        curve.Locked = true;
        Assert.Contains("read-only", Error(curve, curve.Axis, AxisRescaler.Stretch(real, 6000)));
        curve.Locked = false;

        curve.UserLevel = 4;
        Assert.Contains("user level", Error(curve, curve.Axis, AxisRescaler.Stretch(real, 6000)));
        curve.UserLevel = 0;

        var map = (Table3D)rom.GetTable("Map")!;
        Assert.Contains("doesn't belong", Error(map, curve.Axis, real));

        var failed = AxisRescaler.Plan(labels, labels.Axis, [1, 2, 3], BeyondRange.Hold);
        Assert.Throws<InvalidOperationException>(failed.Apply);
        Assert.Equal(before, rom.Binary);
    }

    [Fact]
    public void RefusesToApplyAPlanMadeBeforeTheTableChanged()
    {
        var rom = Load();
        var curve = (Table2D)rom.GetTable("Curve")!;
        var plan = AxisRescaler.Plan(curve, curve.Axis, AxisRescaler.Stretch(Reals(curve.Axis.Cells), 6000), BeyondRange.Hold);
        curve.Cells[3].SetBinValue(99);
        var before = (byte[])rom.Binary.Clone();
        Assert.Throws<InvalidOperationException>(plan.Apply);
        Assert.Equal(before, rom.Binary);
    }

    // ===== helpers =====

    /// <summary>A strictly monotonic axis of distinct integers and random values.</summary>
    private static (double[] Axis, double[] Values) RandomTable(Random rng, int n, bool descending)
    {
        var axis = new SortedSet<double>();
        while (axis.Count < n)
            axis.Add(rng.Next(-500, 5000));
        var a = axis.ToArray();
        if (descending)
            Array.Reverse(a);
        return (a, Enumerable.Range(0, n).Select(_ => rng.NextDouble() * 200 - 50).ToArray());
    }

    /// <summary>A monotonic axis where about a third of the breakpoints repeat the previous one.</summary>
    private static (double[] Axis, double[] Values) RandomTableWithRepeats(Random rng, int n, bool descending)
    {
        var a = new double[n];
        a[0] = rng.Next(-500, 1000);
        for (int i = 1; i < n; i++)
            a[i] = a[i - 1] + (rng.Next(3) == 0 ? 0 : rng.Next(1, 400));
        if (descending)
            Array.Reverse(a);
        return (a, Enumerable.Range(0, n).Select(_ => rng.NextDouble() * 200 - 50).ToArray());
    }
}
