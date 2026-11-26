using System.Text.Json;

namespace PowerOw;

internal sealed record Preferences(string Language = "auto", ThemeMode Theme = ThemeMode.System,
    bool AutomaticUpdates = true, int UpdateIntervalMinutes = 15)
{
    internal static readonly int[] UpdateIntervals = [5, 15, 30, 60, 120, 360, 1440];
    internal static Preferences Load(bool user = true)
    {
        var defaults = new Preferences();
        try
        {
            string ini = Path.Combine(AppContext.BaseDirectory, "Appearance.ini");
            if (File.Exists(ini) && new FileInfo(ini).Length < 2048)
            {
                var values = File.ReadLines(ini).Where(line => line.Contains('='))
                    .Select(line => line.Split('=', 2)).ToArray();
                string? Find(string key) => values.FirstOrDefault(row => row[0].Equals(key, StringComparison.OrdinalIgnoreCase))?[1];
                defaults = new Preferences(ValidLanguage(Find("Language")),
                    Enum.TryParse(Find("Theme"), ignoreCase: true, out ThemeMode result) && Enum.IsDefined(result) ? result : ThemeMode.System);
            }
            if (user && File.Exists(PathForUser) && new FileInfo(PathForUser).Length < 4096)
            {
                var loaded = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(PathForUser));
                if (loaded is not null)
                    return loaded with
                    {
                        Language = ValidLanguage(loaded.Language),
                        Theme = Enum.IsDefined(loaded.Theme) ? loaded.Theme : defaults.Theme,
                        UpdateIntervalMinutes = UpdateIntervals.Contains(loaded.UpdateIntervalMinutes) ?
                            loaded.UpdateIntervalMinutes : defaults.UpdateIntervalMinutes
                    };
            }
        }
        catch (Exception error) when (Faults.Expected(error) || error is JsonException) { }
        return defaults;
    }

    internal void Save()
    {
        string directory = Path.GetDirectoryName(PathForUser)!;
        Directory.CreateDirectory(directory);
        SafePaths.RequirePlain(directory);
        string temporary = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, PathForUser, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private static string PathForUser => Path.Combine(StateStore.Location(StartupScope.User), "interface.json");
    private static string ValidLanguage(string? value) => Array.IndexOf(TextCatalog.Codes, value) >= 0 ? value! : "auto";
}
