using System.IO;
using System.Text.Json;

namespace AemeathCompanion.Engine;

public sealed class CompanionPersistedState
{
    public string Mode { get; set; } = "A";
    public double X { get; set; } = 960;
    public double Y { get; set; } = 540;
    public double SizeMultiplier { get; set; } = 1.0;
    public string? ManualVisual { get; set; }
    public string? ScreenName { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
}

public static class CompanionStateStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AemeathCompanion",
        "state.json");

    public static CompanionPersistedState Load(string? path = null)
    {
        string target = path ?? DefaultPath;
        try
        {
            if (File.Exists(target))
            {
                string json = File.ReadAllText(target);
                var state = JsonSerializer.Deserialize<CompanionPersistedState>(json, JsonOptions);
                if (state is not null)
                {
                    if (state.Mode is not ("A" or "B")) state.Mode = "A";
                    state.SizeMultiplier = Math.Clamp(double.IsFinite(state.SizeMultiplier) ? state.SizeMultiplier : 1.0, 0.25, 2.0);
                    return state;
                }
            }
        }
        catch { }

        return new CompanionPersistedState();
    }

    public static void Save(CompanionPersistedState state, string? path = null)
    {
        string target = path ?? DefaultPath;
        try
        {
            string? dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(target, json);
        }
        catch { }
    }
}
