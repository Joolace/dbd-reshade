using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace DBDReshade.Services;

public sealed record LanguageOption(string Code, string NativeName, int Completion)
{
    /// <summary>"Italiano", or "Polski · 80%" while a translation is incomplete.</summary>
    public string DisplayName => Completion >= 100 ? NativeName : $"{NativeName} · {Completion}%";

    public override string ToString() => DisplayName;
}

/// <summary>
/// Runtime localization from JSON files ({ "key": "text" }).
/// <list type="number">
/// <item>Strings/*.json are embedded in the exe (always available, offline too);</item>
/// <item>translations/*.json downloaded from GitHub override them key by key, or add new languages.</item>
/// </list>
/// English fills any key a language is missing. XAML uses {DynamicResource Key}; code uses Get/Format.
/// </summary>
public static class Loc
{
    private const string Fallback = "en";

    /// <summary>Display order for the bundled languages; downloaded extra languages follow alphabetically.</summary>
    private static readonly string[] PreferredOrder = { "en", "it", "es", "fr", "de", "pt", "ru", "zh", "ja", "ko" };

    private static readonly Dictionary<string, Dictionary<string, string>> Tables = new(StringComparer.OrdinalIgnoreCase);
    private static ResourceDictionary? _active;

    public static IReadOnlyList<LanguageOption> Languages { get; private set; } = Array.Empty<LanguageOption>();

    public static LanguageOption Current { get; private set; } = new(Fallback, "English", 100);

    /// <summary>Raised after the language (or the downloaded translations) changed.</summary>
    public static event EventHandler? Changed;

    /// <summary>The Windows display language if we have it, otherwise English.</summary>
    public static string DefaultCode
    {
        get
        {
            var system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return Tables.ContainsKey(system) ? system : Fallback;
        }
    }

    /// <summary>Loads embedded and downloaded translations. Call once at startup, before <see cref="Apply"/>.</summary>
    public static void Initialize() => LoadTables();

    /// <summary>Re-reads downloaded translations and re-applies the current language.</summary>
    public static void Reload()
    {
        LoadTables();
        Apply(Current.Code);
    }

    public static void Apply(string? code)
    {
        var language = Languages.FirstOrDefault(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
                       ?? Languages.FirstOrDefault(l => l.Code == Fallback)
                       ?? Current;

        // English first, then the chosen language on top: missing keys stay readable.
        var dictionary = new ResourceDictionary();
        foreach (var table in new[] { Fallback, language.Code }.Distinct())
            if (Tables.TryGetValue(table, out var strings))
                foreach (var (key, value) in strings)
                    dictionary[key] = value;

        var resources = Application.Current.Resources;
        if (_active is not null) resources.MergedDictionaries.Remove(_active);
        resources.MergedDictionaries.Add(dictionary);
        _active = dictionary;

        // Montserrat has Latin and Cyrillic only: give CJK languages the right Windows font for their glyphs.
        resources["AppFont"] = FontFor(language.Code);

        Current = language;
        try
        {
            var culture = new CultureInfo(language.Code);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // A community language code Windows doesn't know: keep the previous culture for formatting.
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static string Get(string key) =>
        Application.Current?.TryFindResource(key) is string value ? value.Replace("\\n", "\n") : key;

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>Parses a translation file. Only string values are kept; "_comment"-style keys are skipped.</summary>
    public static bool TryParse(string json, out Dictionary<string, string> strings)
    {
        strings = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String) continue;
                if (property.Name.StartsWith('_') && property.Name != "_language") continue;

                var text = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(text)) strings[property.Name] = text;
            }
            return strings.ContainsKey("_language");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void LoadTables()
    {
        Tables.Clear();

        var assembly = Assembly.GetExecutingAssembly();
        foreach (var resource in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith("strings.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            if (TryParse(reader.ReadToEnd(), out var strings))
                Tables[resource["strings.".Length..^".json".Length]] = strings;
        }

        if (Directory.Exists(AppPaths.TranslationsDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(AppPaths.TranslationsDirectory, "*.json"))
            {
                string json;
                try { json = File.ReadAllText(file); }
                catch (IOException) { continue; }
                if (!TryParse(json, out var downloaded)) continue;

                var code = Path.GetFileNameWithoutExtension(file);
                if (!Tables.TryGetValue(code, out var table))
                    Tables[code] = table = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (key, value) in downloaded) table[key] = value;
            }
        }

        var reference = Tables.TryGetValue(Fallback, out var en)
            ? en.Keys.Where(k => k != "_language").ToList()
            : new List<string>();

        Languages = Tables
            .Select(t => new LanguageOption(
                t.Key,
                t.Value.TryGetValue("_language", out var name) ? name : t.Key,
                reference.Count == 0 ? 100 : (int)Math.Floor(100.0 * reference.Count(t.Value.ContainsKey) / reference.Count)))
            .OrderBy(l => Rank(l.Code))
            .ThenBy(l => l.NativeName, StringComparer.CurrentCulture)
            .ToList();
    }

    private static int Rank(string code)
    {
        var index = Array.IndexOf(PreferredOrder, code);
        return index >= 0 ? index : int.MaxValue;
    }

    private static FontFamily FontFor(string code)
    {
        var cjk = code switch
        {
            "ja" => "Yu Gothic UI, Meiryo UI, Meiryo, ",
            "zh" => "Microsoft YaHei UI, Microsoft YaHei, ",
            "ko" => "Malgun Gothic, ",
            _ => "",
        };
        return new FontFamily(new Uri("pack://application:,,,/"), $"./Assets/Fonts/#Montserrat, {cjk}Segoe UI");
    }
}
