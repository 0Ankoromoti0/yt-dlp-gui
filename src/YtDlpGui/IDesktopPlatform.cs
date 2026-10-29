using YtDlpGui.Core;

namespace YtDlpGui;

public interface IDesktopPlatform
{
    ToolPaths DiscoverTools();
    bool IsManagedEngine(string? path);
    void CopyEngine(string source, string destination);
    Task<string?> SelectFolderAsync(string currentDirectory);
    void OpenFolder(string directory);
    void Post(Action action);
}
