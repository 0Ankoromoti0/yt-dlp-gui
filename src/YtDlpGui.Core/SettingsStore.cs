using System.Text.Json;

namespace YtDlpGui.Core;

public sealed record AppSettings
{
    public string SaveDirectory { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public VideoContainer VideoContainer { get; init; } = VideoContainer.Mp4;
    public AudioOutput AudioOutput { get; init; } = AudioOutput.Original;
    public int AudioBitrate { get; init; } = 192;
    public bool AudioOnly { get; init; }
}

public sealed class SettingsStore
{
    private readonly string _path;
    public SettingsStore(string? directory = null)
    {
        directory ??= Environment.GetEnvironmentVariable("YTDLPGUI_SETTINGS_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YtDlpGui");
        _path = Path.Combine(directory, "settings.json");
    }
    public AppSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new();
            return settings with
            {
                VideoContainer = Enum.IsDefined(settings.VideoContainer) ? settings.VideoContainer : VideoContainer.Mp4,
                AudioOutput = Enum.IsDefined(settings.AudioOutput) ? settings.AudioOutput : AudioOutput.Original,
                AudioBitrate = settings.AudioBitrate is 64 or 96 or 128 or 160 or 192 or 256 or 320 ? settings.AudioBitrate : 192,
                SaveDirectory = string.IsNullOrWhiteSpace(settings.SaveDirectory) ? new AppSettings().SaveDirectory : settings.SaveDirectory
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _path, overwrite: true);
    }
}
