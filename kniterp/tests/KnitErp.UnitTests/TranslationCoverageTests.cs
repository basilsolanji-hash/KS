using System.Text.Json;
using System.Text.RegularExpressions;
using KnitErp.Application.Workspace;
using KnitErp.Domain.Access;

namespace KnitErp.UnitTests;

/// <summary>
/// Полнота переводов интерфейса (D60): каждый текст, обёрнутый в L("…") в коде Web, и названия меню и инструментов
/// есть в переводе каждого языка, а в файлах переводов нет забытых ключей.
/// </summary>
public sealed partial class TranslationCoverageTests
{
    private static readonly string WebDir = Path.Combine(FindRoot(), "src", "KnitErp.Web");

    public static TheoryData<string> Languages => new(UiLanguages.All.Where(l => l.Code != UiLanguages.Default).Select(l => l.Code));

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_interface_text_is_translated(string code)
    {
        var translations = Load(code);
        var missing = UsedKeys().Where(k => !translations.TryGetValue(k, out var t) || string.IsNullOrWhiteSpace(t)).Order().ToList();
        Assert.True(missing.Count == 0, $"{code}.json: нет перевода для {missing.Count}:\n" + string.Join("\n", missing));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Translation_files_have_no_stale_keys(string code)
    {
        var used = UsedKeys();
        var stale = Load(code).Keys.Where(k => !used.Contains(k)).Order().ToList();
        Assert.True(stale.Count == 0, $"{code}.json: ключи не используются в коде:\n" + string.Join("\n", stale));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Placeholders_survive_translation(string code)
    {
        foreach (var (key, value) in Load(code))
        {
            var expected = Placeholder().Matches(key).Select(m => m.Value).Order();
            Assert.Equal(expected, Placeholder().Matches(value).Select(m => m.Value).Order());
        }
    }

    private static HashSet<string> UsedKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(WebDir, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".razor") || f.EndsWith(".cs")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Call().Matches(text))
            {
                keys.Add(Regex.Unescape(m.Groups[1].Value));
            }
        }

        // Названия разделов меню и групп — строки в MainLayout, переводятся при выводе.
        var layout = File.ReadAllText(Path.Combine(WebDir, "Components", "Layout", "MainLayout.razor"));
        foreach (Match m in MenuItem().Matches(layout))
        {
            keys.Add(m.Groups[1].Value);
        }

        foreach (Match m in MenuSection().Matches(layout))
        {
            keys.Add(m.Groups[1].Value);
        }

        foreach (var tool in ToolCatalog.All)
        {
            keys.Add(tool.Title);
            keys.Add(tool.Hint);
        }

        return keys;
    }

    private static Dictionary<string, string> Load(string code) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(WebDir, "Localization", $"{code}.json"))) ?? [];

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "KnitErp.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Не найден KnitErp.slnx");
    }

    [GeneratedRegex("""(?<![\w.])(?:Text\.)?L\("((?:[^"\\]|\\.)*)"[,)]""")]
    private static partial Regex Call();

    [GeneratedRegex("""\("[\w/-]*", "([^"]+)", a =>""")]
    private static partial Regex MenuItem();

    [GeneratedRegex("""\["[\w/-]*"\] = "([^"]+)",""")]
    private static partial Regex MenuSection();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();
}
