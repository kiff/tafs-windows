using System.Globalization;
using System.Text;

namespace Tafs.Core.Kiff;

/// <summary>A JSON value as read by <see cref="Jcs"/>, each number kept as written.</summary>
public abstract record JcsValue
{
    public sealed record Null : JcsValue;
    public sealed record Bool(bool Value) : JcsValue;
    public sealed record Number(string Literal) : JcsValue;
    public sealed record Text(string Value) : JcsValue;
    public sealed record Array(IReadOnlyList<JcsValue> Items) : JcsValue
    {
        public bool Equals(Array? other) => other is not null && Items.SequenceEqual(other.Items);
        public override int GetHashCode() => Items.Count;
    }
    public sealed record Object(IReadOnlyDictionary<string, JcsValue> Fields) : JcsValue
    {
        public bool Equals(Object? other) => other is not null && Fields.Count == other.Fields.Count
            && Fields.All(f => other.Fields.TryGetValue(f.Key, out var v) && v.Equals(f.Value));
        public override int GetHashCode() => Fields.Count;
    }

    public JcsValue? this[string key] => this is Object o && o.Fields.TryGetValue(key, out var v) ? v : null;
    public string? AsString => this is Text t ? t.Value : null;
    public IReadOnlyList<JcsValue>? AsArray => this is Array a ? a.Items : null;
}

/// <summary>
/// RFC 8785 (JSON Canonicalization Scheme), as planque computes a grant's digest and as the iPad's <c>JCS</c>: the app
/// recomputes it from the request it shows, so an approval approves exactly that request. Keys sort by UTF-16 code units,
/// numbers print as ECMAScript does, and an integer beyond 2^53 - 1 is refused, as planque and KIFF refuse it. JSON is read
/// here, not by System.Text.Json, so a number keeps its literal and an integer is told from a fraction, as planque's Go does.
/// </summary>
public static class Jcs
{
    public sealed class Failure(string why) : Exception(why);

    public static byte[] Canonical(byte[] json) => Canonical(Parse(json));

    public static byte[] Canonical(JcsValue value)
    {
        var out_ = new StringBuilder();
        Write(value, out_);
        return Encoding.UTF8.GetBytes(out_.ToString());
    }

    static void Write(JcsValue value, StringBuilder o)
    {
        switch (value)
        {
            case JcsValue.Null: o.Append("null"); break;
            case JcsValue.Bool b: o.Append(b.Value ? "true" : "false"); break;
            case JcsValue.Number n: o.Append(Number(n.Literal)); break;
            case JcsValue.Text t: Quote(t.Value, o); break;
            case JcsValue.Array a:
                o.Append('[');
                for (var i = 0; i < a.Items.Count; i++)
                {
                    if (i > 0) o.Append(',');
                    Write(a.Items[i], o);
                }
                o.Append(']');
                break;
            case JcsValue.Object obj:
                o.Append('{');
                // UTF-16 code units, as RFC 8785 §3.2.3: ordinal comparison of .NET strings is exactly that.
                var first = true;
                foreach (var key in obj.Fields.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    if (!first) o.Append(',');
                    first = false;
                    Quote(key, o);
                    o.Append(':');
                    Write(obj.Fields[key], o);
                }
                o.Append('}');
                break;
        }
    }

    static void Quote(string s, StringBuilder o)
    {
        o.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': o.Append("\\\""); break;
                case '\\': o.Append("\\\\"); break;
                case '\b': o.Append("\\b"); break;
                case '\t': o.Append("\\t"); break;
                case '\n': o.Append("\\n"); break;
                case '\f': o.Append("\\f"); break;
                case '\r': o.Append("\\r"); break;
                case < ' ': o.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); break;
                default: o.Append(c); break;
            }
        }
        o.Append('"');
    }

    const string MaxSafeInteger = "9007199254740991";

    /// <summary>
    /// A number literal in its canonical form. A plain integer beyond 2^53 - 1 is refused, as planque refuses it; one written
    /// with a fraction or an exponent prints as a double.
    /// </summary>
    public static string Number(string literal)
    {
        if (!literal.Any(c => c is '.' or 'e' or 'E'))
        {
            var digits = literal.TrimStart('-');
            while (digits.Length > 1 && digits[0] == '0') digits = digits[1..];
            if (digits.Length > MaxSafeInteger.Length || (digits.Length == MaxSafeInteger.Length && string.CompareOrdinal(digits, MaxSafeInteger) > 0))
                throw new Failure("unsafe integer");
        }
        if (!double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || !double.IsFinite(d)) throw new Failure("not finite");
        return EcmaScript(d);
    }

    /// <summary>
    /// ECMAScript's Number::toString, which RFC 8785 uses for every number. .NET's round-trip form already gives the shortest
    /// digits that round-trip; this only places them.
    /// </summary>
    public static string EcmaScript(double d)
    {
        if (!double.IsFinite(d)) throw new Failure("not finite");
        if (d == 0) return "0";
        if (d < 0) return "-" + EcmaScript(-d);
        var text = d.ToString("R", CultureInfo.InvariantCulture).ToLowerInvariant();
        var parts = text.Split('e', 2);
        var exp = parts.Length > 1 ? int.Parse(parts[1].Replace("+", ""), CultureInfo.InvariantCulture) : 0;
        var pieces = parts[0].Split('.');
        var intPart = pieces[0];
        var digits = intPart + (pieces.Length > 1 ? pieces[1] : "");
        var point = intPart.Length;
        while (digits.Length > 0 && digits[0] == '0') { digits = digits[1..]; point--; }
        digits = digits.TrimEnd('0');
        var k = digits.Length;
        var n = point + exp;
        if (k <= n && n <= 21) return digits + new string('0', n - k);
        if (0 < n && n <= 21) return digits[..n] + "." + digits[n..];
        if (-6 < n && n <= 0) return "0." + new string('0', -n) + digits;
        var e = n - 1;
        var head = k == 1 ? digits : digits[..1] + "." + digits[1..];
        return head + "e" + (e < 0 ? "-" : "+") + Math.Abs(e).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Reads one JSON value (RFC 8259), keeping each number's literal; a repeated key's last value wins, as in Go.</summary>
    public static JcsValue Parse(byte[] data)
    {
        var p = new Parser(data);
        p.Space();
        var v = p.Value(0);
        p.Space();
        if (p.I != data.Length) throw new Failure("not JSON");
        return v;
    }

    sealed class Parser(byte[] b)
    {
        public int I;

        public void Space()
        {
            while (I < b.Length && b[I] is 0x20 or 0x09 or 0x0A or 0x0D) I++;
        }

        void Expect(string s)
        {
            foreach (var c in s)
            {
                if (I >= b.Length || b[I] != c) throw new Failure("not JSON");
                I++;
            }
        }

        public JcsValue Value(int depth)
        {
            if (depth >= 200 || I >= b.Length) throw new Failure("not JSON");
            switch (b[I])
            {
                case (byte)'n': Expect("null"); return new JcsValue.Null();
                case (byte)'t': Expect("true"); return new JcsValue.Bool(true);
                case (byte)'f': Expect("false"); return new JcsValue.Bool(false);
                case (byte)'"': return new JcsValue.Text(String());
                case (byte)'[':
                {
                    I++;
                    Space();
                    var a = new List<JcsValue>();
                    if (I < b.Length && b[I] == ']') { I++; return new JcsValue.Array(a); }
                    while (true)
                    {
                        Space();
                        a.Add(Value(depth + 1));
                        Space();
                        if (I >= b.Length) throw new Failure("not JSON");
                        if (b[I] == ',') { I++; continue; }
                        if (b[I] == ']') { I++; return new JcsValue.Array(a); }
                        throw new Failure("not JSON");
                    }
                }
                case (byte)'{':
                {
                    I++;
                    Space();
                    var o = new Dictionary<string, JcsValue>(StringComparer.Ordinal);
                    if (I < b.Length && b[I] == '}') { I++; return new JcsValue.Object(o); }
                    while (true)
                    {
                        Space();
                        if (I >= b.Length || b[I] != '"') throw new Failure("not JSON");
                        var k = String();
                        Space();
                        Expect(":");
                        Space();
                        o[k] = Value(depth + 1);
                        Space();
                        if (I >= b.Length) throw new Failure("not JSON");
                        if (b[I] == ',') { I++; continue; }
                        if (b[I] == '}') { I++; return new JcsValue.Object(o); }
                        throw new Failure("not JSON");
                    }
                }
                default: return new JcsValue.Number(Number());
            }
        }

        int Digits()
        {
            var s = I;
            while (I < b.Length && b[I] is >= (byte)'0' and <= (byte)'9') I++;
            return I - s;
        }

        string Number()
        {
            var start = I;
            if (I < b.Length && b[I] == '-') I++;
            if (I >= b.Length) throw new Failure("not JSON");
            if (b[I] == '0') I++;
            else if (Digits() == 0) throw new Failure("not JSON");
            if (I < b.Length && b[I] == '.')
            {
                I++;
                if (Digits() == 0) throw new Failure("not JSON");
            }
            if (I < b.Length && b[I] is (byte)'e' or (byte)'E')
            {
                I++;
                if (I < b.Length && b[I] is (byte)'+' or (byte)'-') I++;
                if (Digits() == 0) throw new Failure("not JSON");
            }
            return Encoding.ASCII.GetString(b, start, I - start);
        }

        int Hex4()
        {
            if (I + 4 > b.Length || !int.TryParse(Encoding.ASCII.GetString(b, I, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var v))
                throw new Failure("not JSON");
            I += 4;
            return v;
        }

        string String()
        {
            I++; // the opening quote
            var s = new StringBuilder();
            var run = new List<byte>();
            var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
            void Flush()
            {
                if (run.Count == 0) return;
                try { s.Append(strict.GetString(run.ToArray())); }
                catch (DecoderFallbackException) { throw new Failure("not JSON"); }
                run.Clear();
            }
            while (I < b.Length)
            {
                var c = b[I];
                if (c == '"')
                {
                    I++;
                    Flush();
                    return s.ToString();
                }
                if (c == '\\')
                {
                    Flush();
                    I++;
                    if (I >= b.Length) throw new Failure("not JSON");
                    var e = b[I++];
                    switch (e)
                    {
                        case (byte)'"': s.Append('"'); break;
                        case (byte)'\\': s.Append('\\'); break;
                        case (byte)'/': s.Append('/'); break;
                        case (byte)'b': s.Append('\b'); break;
                        case (byte)'f': s.Append('\f'); break;
                        case (byte)'n': s.Append('\n'); break;
                        case (byte)'r': s.Append('\r'); break;
                        case (byte)'t': s.Append('\t'); break;
                        case (byte)'u':
                            var v = Hex4();
                            if (v is >= 0xD800 and <= 0xDBFF)
                            {
                                // A surrogate pair, written as two escapes.
                                if (I + 1 >= b.Length || b[I] != '\\' || b[I + 1] != 'u') throw new Failure("not JSON");
                                I += 2;
                                var low = Hex4();
                                if (low is < 0xDC00 or > 0xDFFF) throw new Failure("not JSON");
                                s.Append((char)v).Append((char)low);
                            }
                            else if (v is >= 0xDC00 and <= 0xDFFF) throw new Failure("not JSON");
                            else s.Append((char)v);
                            break;
                        default: throw new Failure("not JSON");
                    }
                    continue;
                }
                if (c < 0x20) throw new Failure("not JSON");
                run.Add(c);
                I++;
            }
            throw new Failure("not JSON");
        }
    }
}
