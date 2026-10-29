using YtDlpGui.Core;

namespace YtDlpGui;

// Compiles the same production view model on both operating systems without a GUI dependency.
public sealed class DesktopPlatform : IDesktopPlatform
{
    public string? FolderToSelect { get; set; }
    public string? OpenedFolder { get; private set; }
    public ToolPaths DiscoverTools() => new(null, null, null, null);
    public bool IsManagedEngine(string? path) => false;
    public void CopyEngine(string source, string destination) => File.Copy(source, destination, true);
    public Task<string?> SelectFolderAsync(string currentDirectory) => Task.FromResult(FolderToSelect);
    public void OpenFolder(string directory) => OpenedFolder = directory;
    public void Post(Action action) => action();
}
public static class CommandRefresh
{
    public static event EventHandler? RequerySuggested;
    public static void InvalidateRequerySuggested() => RequerySuggested?.Invoke(null, EventArgs.Empty);
}
