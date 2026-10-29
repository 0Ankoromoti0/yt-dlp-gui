namespace YtDlpGui.Core;

public sealed record ToolPaths(string? YtDlp, string? Ffmpeg, string? Ffprobe, string? Deno)
{
    public bool CanDownload => YtDlp is not null && Ffmpeg is not null && Ffprobe is not null;

    public static ToolPaths Discover(string baseDirectory) => new(
        FindIn(baseDirectory, ExecutableName("yt-dlp")), FindIn(baseDirectory, ExecutableName("ffmpeg")),
        FindIn(baseDirectory, ExecutableName("ffprobe")), FindIn(baseDirectory, ExecutableName("deno")));

    public static string ExecutableName(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    private static string? FindIn(string baseDirectory, string executable)
    {
        var directories = new[] { Path.Combine(baseDirectory, "tools"), baseDirectory }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
        foreach (var directory in directories)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                var path = Path.GetFullPath(Path.Combine(directory.Trim('"'), executable));
                if (File.Exists(path)) return path;
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { }
        }
        return null;
    }
}
