using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using DHud.Services;
using Xunit;

namespace DHud.Tests.Unit;

/// <summary>
/// Dil bütünlüğü: kullanıcı dili değiştirdiğinde ham anahtar ("W.Foo") görünmemeli ve biçim yer tutucuları ({0}) iki dilde uyuşmalı.
/// Kaynak kodu taranarak yeni eklenen ama sözlüğe girilmeyen anahtarlar yakalanır.
/// </summary>
[Trait(Cat.Name, Cat.Fast)]
public partial class LocalizationTests
{
    private static IDictionary Strings()
    {
        var field = typeof(Loc).GetField("Strings", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (IDictionary)field.GetValue(null)!;
    }

    private static (string En, string Tr) Pair(object value)
    {
        var tuple = (System.Runtime.CompilerServices.ITuple)value;
        return ((string)tuple[0]!, (string)tuple[1]!);
    }

    [GeneratedRegex(@"\{(\d+)(?:[:,][^}]*)?\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex("\"([A-Za-z]{1,8}\\.[A-Za-z0-9.]*[A-Za-z0-9])\"")]
    private static partial Regex KeyLiteral();

    [GeneratedRegex(@"\{h:Loc\s+([A-Za-z0-9_.]+)\s*\}")]
    private static partial Regex XamlKey();

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(Repo.Root, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}mobile{Path.DirectorySeparatorChar}")
                        && !f.EndsWith("Loc.cs", StringComparison.Ordinal)
                        && !f.Contains("_wpftmp"));

    [Fact]
    public void Dictionary_is_not_empty_and_has_no_blank_translations()
    {
        var all = Strings();
        Assert.True(all.Count > 300, $"beklenenden az anahtar: {all.Count}");
        foreach (DictionaryEntry entry in all)
        {
            var (en, tr) = Pair(entry.Value!);
            Assert.False(string.IsNullOrWhiteSpace(en), $"{entry.Key}: İngilizce boş");
            Assert.False(string.IsNullOrWhiteSpace(tr), $"{entry.Key}: Türkçe boş");
        }
    }

    [Fact]
    public void Format_placeholders_match_between_english_and_turkish()
    {
        var problems = new List<string>();
        foreach (DictionaryEntry entry in Strings())
        {
            var (en, tr) = Pair(entry.Value!);
            var a = Placeholder().Matches(en).Select(m => m.Groups[1].Value).Distinct().Order().ToList();
            var b = Placeholder().Matches(tr).Select(m => m.Groups[1].Value).Distinct().Order().ToList();
            if (!a.SequenceEqual(b))
            {
                problems.Add($"{entry.Key}: EN {{{string.Join(",", a)}}} / TR {{{string.Join(",", b)}}}");
            }
        }

        Assert.True(problems.Count == 0, "Yer tutucular uyuşmuyor:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Every_string_with_placeholders_formats_without_throwing_in_both_languages()
    {
        var previous = Loc.Current;
        try
        {
            foreach (var language in Enum.GetValues<AppLanguage>())
            {
                Loc.Current = language;
                foreach (DictionaryEntry entry in Strings())
                {
                    var key = (string)entry.Key;
                    var count = Placeholder().Matches(Loc.T(key)).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(-1).Max() + 1;
                    var args = Enumerable.Range(0, count).Select(i => (object)(i + 1)).ToArray();
                    var text = Loc.T(key, args);
                    Assert.False(string.IsNullOrEmpty(text), key);
                }
            }
        }
        finally
        {
            Loc.Current = previous;
        }
    }

    [Fact]
    public void Every_key_used_in_code_and_xaml_exists_in_the_dictionary()
    {
        var all = Strings();
        var keys = all.Keys.Cast<string>().ToHashSet();
        var prefixes = keys.Select(k => k[..k.IndexOf('.')]).ToHashSet();
        var missing = new SortedSet<string>();

        foreach (var file in SourceFiles("*.cs"))
        {
            foreach (Match m in KeyLiteral().Matches(File.ReadAllText(file)))
            {
                var literal = m.Groups[1].Value;
                if (prefixes.Contains(literal[..literal.IndexOf('.')]) && !keys.Contains(literal))
                {
                    missing.Add($"{Path.GetRelativePath(Repo.Root, file)}: \"{literal}\"");
                }
            }
        }

        foreach (var file in SourceFiles("*.xaml"))
        {
            foreach (Match m in XamlKey().Matches(File.ReadAllText(file)))
            {
                if (!keys.Contains(m.Groups[1].Value))
                {
                    missing.Add($"{Path.GetRelativePath(Repo.Root, file)}: {{h:Loc {m.Groups[1].Value}}}");
                }
            }
        }

        Assert.True(missing.Count == 0, "Sözlükte olmayan anahtarlar:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Keys_built_at_runtime_have_every_variant_translated()
    {
        var keys = Strings().Keys.Cast<string>().ToHashSet();
        var expected = new List<string>();
        expected.AddRange(new[] { "Focus", "ShortBreak", "LongBreak" }.SelectMany(p => new[] { $"W.Phase.{p}", $"W.PomodoroNow.{p}" }));
        expected.AddRange(new[] { "clear", "partly", "cloudy", "fog", "drizzle", "rain", "showers", "snow", "thunder" }.Select(k => $"W.Wx.{k}"));
        expected.AddRange(new[] { "clock", "stopwatch", "tools", "weather", "notes" }.Concat(WidgetIds.Extra).Select(k => $"S.Desc.{k}"));
        // WeatherService.Kind her kodda yukarıdaki türlerden birini döndürmeli
        foreach (var code in Enumerable.Range(0, 100))
        {
            Assert.Contains($"W.Wx.{WeatherService.Kind(code)}", expected);
        }

        var missing = expected.Where(k => !keys.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "Eksik: " + string.Join(", ", missing));
    }

    [Fact]
    public void Android_app_strings_exist_in_both_languages_and_every_used_key_is_defined()
    {
        var source = File.ReadAllText(Repo.Path("mobile", "dHud.Mobile", "Services", "Loc.cs"));
        var entries = Regex.Matches(source, "\\[\"(?<k>[^\"]+)\"\\]\\s*=\\s*\\(\\s*(?<en>\"(?:[^\"\\\\]|\\\\.)*\")\\s*,\\s*(?<tr>\"(?:[^\"\\\\]|\\\\.)*\")\\s*\\)");
        Assert.True(entries.Count > 50, $"mobil sözlük çok küçük: {entries.Count}");
        var keys = entries.Select(m => m.Groups["k"].Value).ToHashSet();
        Assert.Equal(entries.Count, keys.Count); // yinelenen anahtar yok

        foreach (Match m in entries)
        {
            var en = Placeholder().Matches(m.Groups["en"].Value).Select(x => x.Groups[1].Value).Distinct().Order();
            var tr = Placeholder().Matches(m.Groups["tr"].Value).Select(x => x.Groups[1].Value).Distinct().Order();
            Assert.True(en.SequenceEqual(tr), $"mobil {m.Groups["k"].Value}: yer tutucular uyuşmuyor");
        }

        var used = Directory.EnumerateFiles(Repo.Path("mobile", "dHud.Mobile"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.EndsWith("Loc.cs"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), "Loc\\.T\\(\"([^\"]+)\"").Select(m => m.Groups[1].Value))
            .ToHashSet();
        var undefined = used.Where(k => !keys.Contains(k)).ToList();
        Assert.True(undefined.Count == 0, "Mobilde tanımsız anahtarlar: " + string.Join(", ", undefined));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    public void Formatting_is_done_with_the_app_language_not_the_machine_culture(string machineCulture)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousLanguage = Loc.Current;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(machineCulture);
            Loc.Current = AppLanguage.English;
            Assert.Equal(Loc.T("W.Feels", "16°"), Loc.T("W.Feels", "16°"));
            Assert.Contains("16°", Loc.T("W.Feels", "16°"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            Loc.Current = previousLanguage;
        }
    }
}
