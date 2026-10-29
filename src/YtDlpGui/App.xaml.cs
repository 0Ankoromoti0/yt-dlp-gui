using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YtDlpGui.Core;

namespace YtDlpGui;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var preview = e.Args.Length == 2 && e.Args[0] == "--render-preview";
        DispatcherUnhandledException += (_, error) =>
        {
            error.Handled = true;
            ReportFatal(error.Exception, preview ? e.Args[1] : null);
            Shutdown(1);
        };
        try
        {
            var window = new MainWindow();
            MainWindow = window;
            if (preview)
            {
                await RenderPreviewAsync(window, e.Args[1]);
                Shutdown(0);
                return;
            }
            window.Show();
            try { await window.ViewModel.InitializeAsync(); }
            catch (Exception ex) { window.ViewModel.ShowError(ex); }
        }
        catch (Exception ex)
        {
            ReportFatal(ex, preview ? e.Args[1] : null);
            Shutdown(1);
        }
    }

    private static void ReportFatal(Exception exception, string? previewDirectory)
    {
        try
        {
            var directory = previewDirectory ?? Environment.GetEnvironmentVariable("YTDLPGUI_SETTINGS_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YtDlpGui");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, previewDirectory is null ? "startup-error.txt" : "preview-error.txt"), exception.ToString());
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        if (previewDirectory is null) MessageBox.Show("アプリを起動できませんでした。\n" + exception.Message,
            "yt-dlp GUI", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static async Task RenderPreviewAsync(MainWindow window, string directory)
    {
        Directory.CreateDirectory(directory);
        var vm = window.ViewModel;
        await vm.InitializeAsync();
        // Fixed preview fixture; never shown during ordinary application use.
        vm.Url = "https://example.com/sample";
        var media = MediaInfo.Parse("""
          {"id":"sample","title":"風景の記録 — 週末の小さな旅","duration":214,"uploader":"Sample video",
           "formats":[
            {"format_id":"a1","url":"https://example.com/a.m4a","ext":"m4a","vcodec":"none","acodec":"mp4a.40.2","abr":128,"asr":44100,"audio_channels":2},
            {"format_id":"v1","url":"https://example.com/v.mp4","ext":"mp4","vcodec":"avc1.640028","acodec":"none","width":1920,"height":1080,"fps":60,"filesize":74000000},
            {"format_id":"v2","url":"https://example.com/v2.mp4","ext":"mp4","vcodec":"avc1.4d401f","acodec":"none","width":1280,"height":720,"fps":30} ]}
          """, vm.Url);
        vm.AudioOnly = false;
        vm.VideoContainer = VideoContainer.Mp4;
        vm.LoadMedia(media);
        vm.SaveDirectory = @"C:\Users\User\Downloads";
        window.Width = 1100; window.Height = 850;
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        Render(window, Path.Combine(directory, "video.png"));
        vm.AudioOnly = true; vm.AudioOutput = AudioOutput.Mp3;
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        Render(window, Path.Combine(directory, "audio.png"));
        if (vm.SelectedVideo is null || vm.VideoChoices.Count != 2 || !vm.ShowBitrate)
            throw new InvalidOperationException("画面状態の検証に失敗しました。");
        vm.Url = "https://example.com/changed";
        if (vm.HasMedia || vm.DownloadCommand.CanExecute(null))
            throw new InvalidOperationException("URL変更後に古い候補が残っています。");
        File.WriteAllText(Path.Combine(directory, "preview-result.txt"), "PASS: video/audio layout rendered; URL invalidation verified.");
    }

    private static void Render(MainWindow window, string path)
    {
        // Render the app's own visual tree without opening an interactive window.
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1100, 820));
        content.Arrange(new Rect(0, 0, 1100, 820));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1100, 820, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
