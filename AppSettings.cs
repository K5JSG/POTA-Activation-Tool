using System.Text.Json;
using System.Text.Json.Serialization;

namespace PotaActivationTool
{
    // The Settings window's choices (menu bar -> Settings...), remembered
    // between runs in the app data folder - same pattern as GpsSettings.
    public class AppSettings
    {
        // ADIF log read automatically after every Load Parks for State. Blank
        // = no fixed file, so the Load ADIF File button shows instead.
        public string AdifPath { get; set; } = "";

        [JsonIgnore]
        public bool HasAdifPath => AdifPath.Trim().Length > 0;

        private const string FileName = "AppSettings.json";

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static AppSettings Load(string folder)
        {
            try
            {
                string path = Path.Combine(folder, FileName);
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
            }
            catch
            {
                // A corrupt/unreadable settings file just falls back to defaults.
            }
            return new AppSettings();
        }

        public void Save(string folder)
        {
            try
            {
                File.WriteAllText(Path.Combine(folder, FileName), JsonSerializer.Serialize(this, JsonOptions));
            }
            catch
            {
                // Best-effort - losing the saved choice isn't worth an error dialog.
            }
        }
    }
}
