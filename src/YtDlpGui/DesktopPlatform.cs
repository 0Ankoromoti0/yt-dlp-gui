using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using YtDlpGui.Core;

namespace YtDlpGui;

public sealed class DesktopPlatform : IDesktopPlatform
{
    public ToolPaths DiscoverTools() => ToolPaths.Discover(AppContext.BaseDirectory);
    public void CopyEngine(string source, string destination) => File.Copy(source, destination, true);
    public bool IsManagedEngine(string? path) => path is not null && Path.GetFullPath(path).StartsWith(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "tools")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public Task<string?> SelectFolderAsync(string currentDirectory)
    {
        var dialog = new OpenFolderDialog { Title = "ファイルの保存先を選択", Multiselect = false };
        if (Directory.Exists(currentDirectory)) dialog.InitialDirectory = currentDirectory;
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FolderName : null);
    }
    public void OpenFolder(string directory)
    {
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        start.ArgumentList.Add(Path.GetFullPath(directory));
        Process.Start(start);
    }
    public void Post(Action action) => Application.Current.Dispatcher.BeginInvoke(action);
}

public static class CommandRefresh
{
    public static event EventHandler? RequerySuggested
    { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public static void InvalidateRequerySuggested() => CommandManager.InvalidateRequerySuggested();
}
