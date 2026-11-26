using System.Globalization;
using System.Reflection;
using System.Text;

namespace PowerOw;

internal sealed class TextCatalog
{
    internal static readonly string[] Codes = ["en", "pl", "de", "ru", "hr", "fr", "es", "zh-CN"];
    internal static readonly string[] Languages = ["English", "Polski", "Deutsch", "Русский", "Hrvatski", "Français", "Español", "简体中文"];
    private static readonly Dictionary<string, string[]> Strings = Load();
    private readonly int index;
    internal CultureInfo Culture { get; }

    internal TextCatalog(string language)
    {
        string effective = language == "auto" ? FromWindows(CultureInfo.CurrentUICulture) : language;
        index = Math.Max(0, Array.IndexOf(Codes, effective));
        Culture = CultureInfo.GetCultureInfo(Codes[index]);
    }

    internal static string FromWindows(CultureInfo culture)
    {
        string language = culture.TwoLetterISOLanguageName;
        if (language == "zh")
            return culture.Name is "zh-CN" or "zh-SG" or "zh-Hans" or "zh-Hans-CN" or "zh" ? "zh-CN" : "en";
        return Array.IndexOf(Codes, language) >= 0 ? language : "en";
    }

    internal string this[string key] => Strings.TryGetValue(key, out var values) ? values[index] : key;
    internal string Format(string key, params object[] values) => string.Format(Culture, this[key], values);
    internal static IEnumerable<KeyValuePair<string, string[]>> All => Strings;
    internal static string Plain(string text)
    {
        if (text.EndsWith(")", StringComparison.Ordinal) && text.Length >= 4 &&
            text[text.Length - 4] == '(' && text[text.Length - 3] == '&')
            return text.Substring(0, text.Length - 4).TrimEnd();
        return text.Replace("&", "");
    }

    private static Dictionary<string, string[]> Load()
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var assembly = typeof(TextCatalog).Assembly;
        foreach (string file in assembly.GetManifestResourceNames().Where(name => name.EndsWith(".tsv", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(file)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Trim().Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                string[] columns = line.Split('\t');
                if (columns.Length != Codes.Length + 1 || columns.Any(string.IsNullOrWhiteSpace))
                    throw new InvalidDataException("Invalid translation row: " + columns[0]);
                result.Add(columns[0], columns.Skip(1).Select(value => value.Replace("\\n", Environment.NewLine)).ToArray());
            }
        }
        return result;
    }
}

