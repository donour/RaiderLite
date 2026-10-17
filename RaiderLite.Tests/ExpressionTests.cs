using System.Text.RegularExpressions;
using RaiderLite.Core.Expressions;

namespace RaiderLite.Tests;

public class ExpressionTests
{
    [Theory]
    [InlineData("x", 7, 7)]
    [InlineData("x*5/8-40", 64, 0)]
    [InlineData("(x*5/8)-40", 64, 0)]
    [InlineData("x*100/255", 255, 100)]
    [InlineData("-2^2", 0, -4)]
    [InlineData("2^3^2", 0, 512)]
    [InlineData("2^-1", 0, 0.5)]
    [InlineData("-x", 3, -3)]
    [InlineData("x - -1", 3, 4)]
    [InlineData("10 % 4", 0, 2)]
    [InlineData("-7 % 3", 0, -1)]
    [InlineData("1.5e2", 0, 150)]
    [InlineData(".5*x", 4, 2)]
    [InlineData("x > 3", 4, 1)]
    [InlineData("x > 3 && x < 5", 4, 1)]
    [InlineData("x == 4 || x == 5", 6, 0)]
    [InlineData("!x", 0, 1)]
    [InlineData("x != 0", 0, 0)]
    [InlineData("1 + 2 * 3", 0, 7)]
    [InlineData("(1 + 2) * 3", 0, 9)]
    public void Evaluates(string expr, double x, double expected)
    {
        Assert.Equal(expected, ExpressionEngine.Evaluate(expr, x), 9);
    }

    [Theory]
    [InlineData("sqrt(x)", 16, 4)]
    [InlineData("abs(x)", -2, 2)]
    [InlineData("log(x)", 1000, 3)]
    [InlineData("ln(x)", 1, 0)]
    [InlineData("exp(0)", 0, 1)]
    [InlineData("pow(x, 2)", 3, 9)]
    [InlineData("mod(x, 3)", 10, 1)]
    [InlineData("round(x)", 2.5, 3)]
    [InlineData("round(x, 1)", 2.25, 2.3)]
    [InlineData("floor(x)", 2.7, 2)]
    [InlineData("ceil(x)", 2.1, 3)]
    [InlineData("if(x, 1, 2)", 1, 1)]
    [InlineData("if(x, 1, 2)", 0, 2)]
    [InlineData("atan2(1, 1)*4", 0, Math.PI)]
    [InlineData("BitWise(24, x, 1)", 0xFF, 24)]
    [InlineData("BitWise(4, x, 5)", 0xF0, 0x0F)]
    [InlineData("BitWise(0, x, 7)", 0, -1)]
    [InlineData("0x10 + x", 1, 17)]
    public void Functions(string expr, double x, double expected)
    {
        Assert.Equal(expected, ExpressionEngine.Evaluate(expr, x), 9);
    }

    [Theory]
    [InlineData("x +")]
    [InlineData("(x")]
    [InlineData("y")]
    [InlineData("foo(x)")]
    [InlineData("sqrt(x, 2)")]
    public void InvalidExpressionsReturnNaN(string expr)
    {
        Assert.NotNull(ExpressionEngine.Validate(expr));
        Assert.True(double.IsNaN(ExpressionEngine.Evaluate(expr, 1)));
    }

    [RequiresFilesFact(nameof(TestData.LotusDefs))]
    public void EveryExpressionInTheLotusDefinitionsParses()
    {
        var attr = new Regex("(?:expression|to_byte)=\"([^\"]*)\"");
        var failures = new List<string>();
        foreach (var file in Directory.GetFiles(TestData.LotusDefs, "*.xml"))
        {
            foreach (Match m in attr.Matches(File.ReadAllText(file)))
            {
                var expr = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
                // Known typo in T4E090_defs.xml ("inj: afr base" Lambda to_byte, missing '*').
                if (expr == "((x14.6)-5)*20")
                    continue;
                if (ExpressionEngine.Validate(expr) is { } error)
                    failures.Add($"{Path.GetFileName(file)}: {error}");
            }
        }
        Assert.Empty(failures.Distinct());
    }
}
