using RaiderLite.Core.Expressions;

namespace RaiderLite.Core.Model;

/// <summary>
/// Converts between raw ("bin") values and real-world values for a table.
/// Port of RomRaider's Scale.
/// </summary>
public sealed class Scale
{
    public const string RawValueCategory = "Raw Value";

    private readonly Dictionary<double, double> _approxCache = new();
    private const int MaxApproxCacheSize = 100;
    private string _expression = "x";
    private string? _byteExpression = "x";

    /// <summary>Groups like scalings (Metric, Imperial, ...). Shown in the scale selector.</summary>
    public string Category { get; set; } = RawValueCategory;

    /// <summary>Name used by <c>base=</c> references; defaults to the units.</summary>
    public string Name { get; set; } = RawValueCategory;

    public string Unit { get; set; } = "raw value";

    /// <summary>Raw to real expression in <c>x</c>.</summary>
    public string Expression
    {
        get => _expression;
        set
        {
            _expression = value;
            _approxCache.Clear();
        }
    }

    /// <summary>Real to raw expression in <c>x</c>, or null to approximate by searching.</summary>
    public string? ByteExpression
    {
        get => _byteExpression;
        set => _byteExpression = string.IsNullOrEmpty(value) ? null : value;
    }

    public string Format { get; set; } = "#.##";
    public double CoarseIncrement { get; set; } = 2;
    public double FineIncrement { get; set; } = 1;
    public double Min { get; set; }
    public double Max { get; set; }

    public bool IsRaw => Category.Equals(RawValueCategory, StringComparison.OrdinalIgnoreCase);

    public Scale Clone()
    {
        return new Scale
        {
            Category = Category,
            Name = Name,
            Unit = Unit,
            _expression = _expression,
            _byteExpression = _byteExpression,
            Format = Format,
            CoarseIncrement = CoarseIncrement,
            FineIncrement = FineIncrement,
            Min = Min,
            Max = Max,
        };
    }

    public double ToReal(double bin) => ExpressionEngine.Evaluate(Expression, bin);

    /// <summary>Converts a real value to raw. Non-float results are not rounded here.</summary>
    public double ToBin(double real, int storageType, bool signed)
    {
        if (Expression.Trim().Equals("x", StringComparison.OrdinalIgnoreCase))
            return real;
        if (ByteExpression == null)
            return ApproximateToByte(real, storageType, signed);
        double bin = ExpressionEngine.Evaluate(ByteExpression, real);
        // A to_byte that doesn't parse (a definition typo) falls back to searching.
        if (double.IsNaN(bin) && ExpressionEngine.Validate(ByteExpression) != null)
            return ApproximateToByte(real, storageType, signed);
        return bin;
    }

    /// <summary>
    /// Checks that expression and to_byte are inverses (RomRaider tests the value 5).
    /// </summary>
    public bool Validate()
    {
        if (ByteExpression == null)
            return true;
        if (Expression == "x" && ByteExpression == "x")
            return true;
        // An unparsable to_byte is reported by the loader and replaced by searching.
        if (ExpressionEngine.Validate(ByteExpression) != null)
            return true;
        const double startValue = 5;
        double toReal = ExpressionEngine.Evaluate(Expression, startValue);
        double endValue = ExpressionEngine.Evaluate(ByteExpression, toReal);
        return Math.Abs(endValue - startValue) <= .001;
    }

    /// <summary>
    /// Binary search for the raw value whose real value is closest to <paramref name="input"/>,
    /// used when a scaling has no to_byte expression.
    /// </summary>
    public double ApproximateToByte(double input, int storageType, bool signed)
    {
        if (_approxCache.TryGetValue(input, out var cached))
            return cached;

        int bytes = StorageType.ByteLength(storageType);
        double range = Math.Pow(2, 8 * Math.Min(bytes, 4));
        double minValue = 0;
        double maxValue = range - 1;
        if (signed)
        {
            minValue = -range / 2;
            maxValue = range / 2 - 1;
        }

        double currentStep = Math.Floor((maxValue - minValue) / 2) + minValue;
        double stepSize = range / 2;
        const double epsilon = 0.00001;
        double best = currentStep;
        double bestError = double.MaxValue;

        while (stepSize >= 1)
        {
            double minus = ToReal(currentStep - stepSize);
            double plus = ToReal(currentStep + stepSize);
            double plusError = Math.Abs(plus - input);
            double minusError = Math.Abs(minus - input);
            double error;
            if (plusError < minusError)
            {
                currentStep += stepSize;
                error = plusError;
            }
            else
            {
                currentStep -= stepSize;
                error = minusError;
            }

            if (error < bestError)
            {
                bestError = error;
                best = currentStep;
            }

            if (error < epsilon)
                break;
            stepSize = Math.Floor(stepSize / 2);
        }

        best = Math.Clamp(best, minValue, maxValue);
        if (_approxCache.Count < MaxApproxCacheSize)
            _approxCache[input] = best;
        return best;
    }

    public override string ToString() => $"{Category} ({Unit})";
}
