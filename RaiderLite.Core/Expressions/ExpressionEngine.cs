using System.Collections.Concurrent;
using System.Globalization;

namespace RaiderLite.Core.Expressions;

/// <summary>
/// Evaluates RomRaider scaling expressions. RomRaider uses the JEP library; this is a
/// small re-implementation of the subset of JEP grammar and functions that definitions
/// use, with <c>x</c> as the only variable.
/// </summary>
/// <remarks>
/// Precedence follows JEP (lowest first): <c>||</c>, <c>&amp;&amp;</c>, <c>== !=</c>,
/// <c>&lt; &gt; &lt;= &gt;=</c>, <c>+ -</c>, <c>* / %</c>, unary <c>+ - !</c>, <c>^</c>.
/// As in JEP, <c>-2^2</c> is <c>-4</c> and <c>^</c> is right associative.
/// </remarks>
public static class ExpressionEngine
{
    private static readonly ConcurrentDictionary<string, Func<double, double>> Cache = new();

    /// <summary>Evaluates <paramref name="expression"/> with <c>x</c> = <paramref name="x"/>.
    /// Returns NaN if the expression cannot be parsed.</summary>
    public static double Evaluate(string expression, double x)
    {
        var fn = Cache.GetOrAdd(expression, static e =>
        {
            try { return Compile(e); }
            catch (ExpressionException) { return static _ => double.NaN; }
        });
        return fn(x);
    }

    /// <summary>Parses an expression, throwing <see cref="ExpressionException"/> on error.</summary>
    public static Func<double, double> Compile(string expression)
    {
        var parser = new Parser(expression);
        return parser.ParseAll();
    }

    /// <summary>Returns null if the expression parses, otherwise the error message.</summary>
    public static string? Validate(string expression)
    {
        try
        {
            Compile(expression);
            return null;
        }
        catch (ExpressionException ex)
        {
            return ex.Message;
        }
    }

    private sealed class Parser(string text)
    {
        // Limits that keep a malformed definition from overflowing the stack (which would end the
        // process): nesting depth while parsing, and overall length (which bounds evaluation depth).
        private const int MaxDepth = 100;
        private const int MaxLength = 4096;
        private int _pos;
        private int _depth;

        public Func<double, double> ParseAll()
        {
            if (text.Length > MaxLength)
                throw new ExpressionException($"Expression is longer than {MaxLength} characters");
            var result = ParseOr();
            SkipWhitespace();
            if (_pos < text.Length)
                throw Error($"Unexpected '{text[_pos]}'");
            return result;
        }

        private ExpressionException Error(string message) =>
            new($"{message} at position {_pos + 1} in \"{text}\"");

        private void SkipWhitespace()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos]))
                _pos++;
        }

        private bool Match(string token)
        {
            SkipWhitespace();
            if (string.CompareOrdinal(text, _pos, token, 0, token.Length) != 0)
                return false;
            _pos += token.Length;
            return true;
        }

        private char Peek()
        {
            SkipWhitespace();
            return _pos < text.Length ? text[_pos] : '\0';
        }

        private static double Bool(bool b) => b ? 1.0 : 0.0;

        private Func<double, double> ParseOr()
        {
            using var _ = Nest();
            var left = ParseAnd();
            while (Match("||"))
            {
                var l = left;
                var r = ParseAnd();
                left = x => Bool(l(x) != 0 || r(x) != 0);
            }
            return left;
        }

        private Func<double, double> ParseAnd()
        {
            var left = ParseEquality();
            while (Match("&&"))
            {
                var l = left;
                var r = ParseEquality();
                left = x => Bool(l(x) != 0 && r(x) != 0);
            }
            return left;
        }

        private Func<double, double> ParseEquality()
        {
            var left = ParseRelational();
            while (true)
            {
                var l = left;
                if (Match("=="))
                {
                    var r = ParseRelational();
                    left = x => Bool(l(x) == r(x));
                }
                else if (Match("!="))
                {
                    var r = ParseRelational();
                    left = x => Bool(l(x) != r(x));
                }
                else
                {
                    return left;
                }
            }
        }

        private Func<double, double> ParseRelational()
        {
            var left = ParseAdditive();
            while (true)
            {
                var l = left;
                if (Match("<="))
                {
                    var r = ParseAdditive();
                    left = x => Bool(l(x) <= r(x));
                }
                else if (Match(">="))
                {
                    var r = ParseAdditive();
                    left = x => Bool(l(x) >= r(x));
                }
                else if (Match("<"))
                {
                    var r = ParseAdditive();
                    left = x => Bool(l(x) < r(x));
                }
                else if (Match(">"))
                {
                    var r = ParseAdditive();
                    left = x => Bool(l(x) > r(x));
                }
                else
                {
                    return left;
                }
            }
        }

        private Func<double, double> ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (true)
            {
                var l = left;
                if (Match("+"))
                {
                    var r = ParseMultiplicative();
                    left = x => l(x) + r(x);
                }
                else if (Match("-"))
                {
                    var r = ParseMultiplicative();
                    left = x => l(x) - r(x);
                }
                else
                {
                    return left;
                }
            }
        }

        private Func<double, double> ParseMultiplicative()
        {
            var left = ParseUnary();
            while (true)
            {
                var l = left;
                if (Match("*"))
                {
                    var r = ParseUnary();
                    left = x => l(x) * r(x);
                }
                else if (Match("/"))
                {
                    var r = ParseUnary();
                    left = x => l(x) / r(x);
                }
                else if (Match("%"))
                {
                    var r = ParseUnary();
                    left = x => JavaMod(l(x), r(x));
                }
                else
                {
                    return left;
                }
            }
        }

        // Java's % on doubles keeps the sign of the dividend, same as C#'s %.
        private static double JavaMod(double a, double b) => a % b;

        private DepthScope Nest()
        {
            if (++_depth > MaxDepth)
                throw Error($"Expression is nested more than {MaxDepth} levels deep");
            return new DepthScope(this);
        }

        private readonly struct DepthScope(Parser parser) : IDisposable
        {
            public void Dispose() => parser._depth--;
        }

        private Func<double, double> ParseUnary()
        {
            using var _ = Nest();
            if (Match("+"))
                return ParseUnary();
            if (Match("-"))
            {
                var operand = ParseUnary();
                return x => -operand(x);
            }
            if (Peek() == '!' && !(_pos + 1 < text.Length && text[_pos + 1] == '='))
            {
                _pos++;
                var operand = ParseUnary();
                return x => Bool(operand(x) == 0);
            }
            return ParsePower();
        }

        private Func<double, double> ParsePower()
        {
            var b = ParsePrimary();
            if (Match("^"))
            {
                var e = ParseUnary();
                return x => Math.Pow(b(x), e(x));
            }
            return b;
        }

        private Func<double, double> ParsePrimary()
        {
            var c = Peek();
            if (c == '(')
            {
                _pos++;
                var inner = ParseOr();
                if (!Match(")"))
                    throw Error("Expected ')'");
                return inner;
            }
            if (char.IsDigit(c) || c == '.')
                return ParseNumber();
            if (char.IsLetter(c) || c == '_')
                return ParseIdentifier();
            if (c == '\0')
                throw Error("Unexpected end of expression");
            throw Error($"Unexpected '{c}'");
        }

        private Func<double, double> ParseNumber()
        {
            int start = _pos;
            // Hex literals are not JEP syntax but are harmless and handy.
            if (text[_pos] == '0' && _pos + 1 < text.Length && (text[_pos + 1] == 'x' || text[_pos + 1] == 'X'))
            {
                _pos += 2;
                int hexStart = _pos;
                while (_pos < text.Length && Uri.IsHexDigit(text[_pos]))
                    _pos++;
                if (_pos == hexStart)
                    throw Error("Malformed hex number");
                double hv = Convert.ToUInt64(text[hexStart.._pos], 16);
                return _ => hv;
            }

            while (_pos < text.Length && (char.IsDigit(text[_pos]) || text[_pos] == '.'))
                _pos++;
            if (_pos < text.Length && (text[_pos] == 'e' || text[_pos] == 'E'))
            {
                int save = _pos;
                _pos++;
                if (_pos < text.Length && (text[_pos] == '+' || text[_pos] == '-'))
                    _pos++;
                if (_pos < text.Length && char.IsDigit(text[_pos]))
                {
                    while (_pos < text.Length && char.IsDigit(text[_pos]))
                        _pos++;
                }
                else
                {
                    _pos = save;
                }
            }
            var s = text[start.._pos];
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw Error($"Malformed number '{s}'");
            return _ => value;
        }

        private Func<double, double> ParseIdentifier()
        {
            int start = _pos;
            while (_pos < text.Length && (char.IsLetterOrDigit(text[_pos]) || text[_pos] == '_'))
                _pos++;
            var name = text[start.._pos];

            if (Peek() == '(')
            {
                _pos++;
                var args = new List<Func<double, double>>();
                if (!Match(")"))
                {
                    do
                    {
                        args.Add(ParseOr());
                    } while (Match(","));
                    if (!Match(")"))
                        throw Error("Expected ')'");
                }
                return MakeFunction(name, args);
            }

            switch (name)
            {
                case "x":
                    return static x => x;
                case "pi":
                    return static _ => Math.PI;
                case "e":
                    return static _ => Math.E;
                default:
                    throw Error($"Unknown variable '{name}'");
            }
        }

        private Func<double, double> MakeFunction(string name, List<Func<double, double>> a)
        {
            void Arity(int n)
            {
                if (a.Count != n)
                    throw Error($"Function '{name}' expects {n} argument(s), got {a.Count}");
            }

            Func<double, double> Unary(Func<double, double> f)
            {
                Arity(1);
                var a0 = a[0];
                return x => f(a0(x));
            }

            Func<double, double> Binary(Func<double, double, double> f)
            {
                Arity(2);
                var a0 = a[0];
                var a1 = a[1];
                return x => f(a0(x), a1(x));
            }

            switch (name)
            {
                case "sin": return Unary(Math.Sin);
                case "cos": return Unary(Math.Cos);
                case "tan": return Unary(Math.Tan);
                case "asin": return Unary(Math.Asin);
                case "acos": return Unary(Math.Acos);
                case "atan": return Unary(Math.Atan);
                case "sinh": return Unary(Math.Sinh);
                case "cosh": return Unary(Math.Cosh);
                case "tanh": return Unary(Math.Tanh);
                case "asinh": return Unary(Math.Asinh);
                case "acosh": return Unary(Math.Acosh);
                case "atanh": return Unary(Math.Atanh);
                case "log": return Unary(Math.Log10);
                case "ln": return Unary(Math.Log);
                case "exp": return Unary(Math.Exp);
                case "sqrt": return Unary(Math.Sqrt);
                case "abs": return Unary(Math.Abs);
                case "floor": return Unary(Math.Floor);
                case "ceil": return Unary(Math.Ceiling);
                case "atan2": return Binary(Math.Atan2);
                case "pow": return Binary(Math.Pow);
                case "mod": return Binary(JavaMod);
                case "round":
                    if (a.Count == 1)
                        return Unary(JavaRound);
                    return Binary((v, places) =>
                    {
                        double scale = Math.Pow(10, places);
                        return JavaRound(v * scale) / scale;
                    });
                case "min": return Binary(Math.Min);
                case "max": return Binary(Math.Max);
                case "rand":
                    Arity(0);
                    return static _ => Random.Shared.NextDouble();
                case "if":
                    if (a.Count is not (3 or 4))
                        throw Error("Function 'if' expects 3 or 4 arguments");
                    {
                        var cond = a[0];
                        var pos = a[1];
                        var neg = a[2];
                        var zero = a.Count == 4 ? a[3] : null;
                        return x =>
                        {
                            double c = cond(x);
                            if (c > 0) return pos(x);
                            if (c < 0 || zero == null) return neg(x);
                            return zero(x);
                        };
                    }
                case "sum":
                case "avg":
                    if (a.Count == 0)
                        throw Error($"Function '{name}' expects at least one argument");
                    {
                        var args = a.ToArray();
                        bool avg = name == "avg";
                        return x =>
                        {
                            double s = 0;
                            foreach (var f in args) s += f(x);
                            return avg ? s / args.Length : s;
                        };
                    }
                case "BitWise":
                    Arity(3);
                    {
                        var maskF = a[0];
                        var valueF = a[1];
                        var opF = a[2];
                        return x => BitWise(maskF(x), valueF(x), opF(x));
                    }
                default:
                    throw Error($"Unknown function '{name}'");
            }
        }

        // JEP/Java Math.round: floor(v + 0.5).
        private static double JavaRound(double v) => Math.Floor(v + 0.5);

        /// <summary>RomRaider's BitWise(mask, x, op) extension function.</summary>
        private static double BitWise(double maskD, double valueD, double opD)
        {
            int mask = (int)maskD;
            int value = (int)valueD;
            return (int)opD switch
            {
                1 => value & mask,
                2 => value | mask,
                3 => value ^ mask,
                4 => value << mask,
                5 => value >> mask,
                6 => (int)((uint)value >> mask),
                7 => ~value,
                _ => 0,
            };
        }
    }
}

public sealed class ExpressionException(string message) : Exception(message);
