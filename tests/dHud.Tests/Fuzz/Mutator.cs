using System.Text;

namespace DHud.Tests.Fuzz;

/// <summary>
/// Tohumlu (tekrarlanabilir) bozma motoru. Geçerli bir örneği rastgele bozar: bayt çevirme, kesme, araya çöp koyma,
/// sayıları uç değerlerle değiştirme, derin iç içe yapı, dev dizgi. Hata çıkarsa tohum mesajda yazılır.
/// </summary>
internal sealed class Mutator(int seed)
{
    private static readonly string[] Extremes =
    [
        "0", "-1", "-999999999", "2147483647", "-2147483648", "9223372036854775807", "1e308", "-1e308", "1e-320",
        "NaN", "Infinity", "-Infinity", "null", "true", "false", "\"\"", "[]", "{}", "\"\\u0000\"", "0.30000000000000004"
    ];

    public Random Random { get; } = new(seed);

    public int Seed { get; } = seed;

    public string Mutate(string text, int rounds = 0)
    {
        rounds = rounds == 0 ? 1 + Random.Next(4) : rounds;
        for (var i = 0; i < rounds && text.Length > 0; i++)
        {
            text = Random.Next(7) switch
            {
                0 => FlipChar(text),
                1 => Truncate(text),
                2 => InsertGarbage(text),
                3 => ReplaceNumber(text),
                4 => DeleteSpan(text),
                5 => DuplicateSpan(text),
                _ => ReplaceValue(text)
            };
        }

        return text;
    }

    public byte[] MutateBytes(byte[] data)
    {
        var copy = (byte[])data.Clone();
        for (var i = 0; i < 1 + Random.Next(8) && copy.Length > 0; i++)
        {
            copy[Random.Next(copy.Length)] = (byte)Random.Next(256);
        }

        return Random.Next(4) == 0 ? copy[..Random.Next(copy.Length + 1)] : copy;
    }

    public string RandomLine(int maxLength = 300)
    {
        var length = Random.Next(maxLength);
        var builder = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            builder.Append(Random.Next(5) == 0 ? "{}[]\":,"[Random.Next(7)] : (char)Random.Next(32, 0x250));
        }

        return builder.ToString();
    }

    public static string Deep(int depth, string open = "[", string close = "]") =>
        string.Concat(Enumerable.Repeat(open, depth)) + string.Concat(Enumerable.Repeat(close, depth));

    private string FlipChar(string text)
    {
        var chars = text.ToCharArray();
        chars[Random.Next(chars.Length)] = (char)Random.Next(1, 0x300);
        return new string(chars);
    }

    private string Truncate(string text) => text[..Random.Next(text.Length)];

    private string InsertGarbage(string text)
    {
        var garbage = Random.Next(3) switch
        {
            0 => new string((char)Random.Next(33, 0x7f), 1 + Random.Next(20000)),
            1 => Deep(1 + Random.Next(600)),
            _ => RandomLine(60)
        };
        return text.Insert(Random.Next(text.Length + 1), garbage);
    }

    private string ReplaceNumber(string text)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(text, @"-?\d+(\.\d+)?");
        if (matches.Count == 0)
        {
            return FlipChar(text);
        }

        var match = matches[Random.Next(matches.Count)];
        return text[..match.Index] + Extremes[Random.Next(Extremes.Length)] + text[(match.Index + match.Length)..];
    }

    private string DeleteSpan(string text)
    {
        var start = Random.Next(text.Length);
        return text.Remove(start, Math.Min(text.Length - start, 1 + Random.Next(40)));
    }

    private string DuplicateSpan(string text)
    {
        var start = Random.Next(text.Length);
        var span = text.Substring(start, Math.Min(text.Length - start, 1 + Random.Next(200)));
        return text.Insert(start, span);
    }

    private string ReplaceValue(string text)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(text, @"(?<=:\s*)(""[^""]*""|true|false|null|\[[^\]]*\]|\{[^}]*\})");
        if (matches.Count == 0)
        {
            return InsertGarbage(text);
        }

        var match = matches[Random.Next(matches.Count)];
        return text[..match.Index] + Extremes[Random.Next(Extremes.Length)] + text[(match.Index + match.Length)..];
    }
}
