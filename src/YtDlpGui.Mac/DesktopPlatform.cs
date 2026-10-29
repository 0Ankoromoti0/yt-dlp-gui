using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using YtDlpGui.Core;

namespace YtDlpGui;

public sealed class DesktopPlatform : IDesktopPlatform
{
    // Keep the signed bundle immutable; the independently updatable engine lives in user data.
    private static string EngineDirectory => Path.Combine(
        Environment.GetEnvironmentVariable("YTDLPGUI_SETTINGS_DIR") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YtDlpGui"), "tools");
    public ToolPaths DiscoverTools()
    {
        var tools = ToolPaths.Discover(AppContext.BaseDirectory);
        var engine = Path.Combine(EngineDirectory, ToolPaths.ExecutableName("yt-dlp"));
        if (!File.Exists(engine) && tools.YtDlp is { } bundled && IsBundledEngine(bundled))
        {
            Directory.CreateDirectory(EngineDirectory);
            // A unique temporary file prevents concurrent launches from exposing a partial executable.
            var temporary = engine + "." + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(bundled, temporary);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, File.GetUnixFileMode(bundled));
                try { File.Move(temporary, engine); }
                catch (IOException) when (File.Exists(engine)) { }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return File.Exists(engine) ? tools with { YtDlp = engine } : tools;
    }
    private static bool IsBundledEngine(string path) => Path.GetFullPath(path).StartsWith(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "tools")) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    public bool IsManagedEngine(string? path) => path is not null && Path.GetFullPath(path) == Path.Combine(EngineDirectory, ToolPaths.ExecutableName("yt-dlp"));
    public async Task<string?> SelectFolderAsync(string currentDirectory)
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
            ?? throw new InvalidOperationException("保存先を選択するウィンドウがありません。");
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new()
        {
            Title = "ファイルの保存先を選択", AllowMultiple = false,
            SuggestedStartLocation = Directory.Exists(currentDirectory)
                ? await window.StorageProvider.TryGetFolderFromPathAsync(Path.GetFullPath(currentDirectory)) : null
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
    public void OpenFolder(string directory)
    {
        var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        start.ArgumentList.Add(Path.GetFullPath(directory));
        Process.Start(start);
    }
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}

public static class CommandRefresh
{
    public static event EventHandler? RequerySuggested;
    public static void InvalidateRequerySuggested() => RequerySuggested?.Invoke(null, EventArgs.Empty);
}
