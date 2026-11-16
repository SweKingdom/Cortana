using System.IO;
using System.Text.Json;

namespace Companion.Core;

/// <summary>Minimal settings.json (step 1: window position only). Lives in %APPDATA%\Companion\.</summary>
internal sealed class Settings
{
    public int Version { get; set; } = 1;
    /// <summary>Window position in physical screen pixels; null = use default corner.</summary>
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Companion");
    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new Settings();
            try
            {
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new Settings();
            }
            catch (JsonException)
            {
                // D-047: corrupt JSON -> keep a copy, start from defaults.
                File.Copy(FilePath, Path.Combine(Dir, "settings.unreadable.json"), overwrite: true);
                return new Settings();
            }
        }
        catch (IOException) { return new Settings(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (IOException) { /* position is not worth crashing for */ }
    }
}
