using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using YtDlpGui.Core;

namespace YtDlpGui;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();
    public MainWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
        Closing += (_, _) => { if (ViewModel.IsBusy) ViewModel.Cancel(); ViewModel.SaveSettings(); };
    }

    internal async Task SmokeTestAsync()
    {
        if (ViewModel.HasError) throw new InvalidOperationException(ViewModel.ErrorMessage);
        var fixture = MediaInfo.Parse("""
          {"id":"sample","title":"風景の記録 — 週末の小さな旅","duration":214,"uploader":"Sample video",
           "formats":[
            {"format_id":"a1","url":"https://example.com/a.m4a","ext":"m4a","vcodec":"none","acodec":"mp4a.40.2","abr":128},
            {"format_id":"v1","url":"https://example.com/v.mp4","ext":"mp4","vcodec":"avc1.640028","acodec":"none","width":1920,"height":1080,"fps":60},
            {"format_id":"v2","url":"https://example.com/v2.mp4","ext":"mp4","vcodec":"avc1.4d401f","acodec":"none","width":1280,"height":720,"fps":30}]}
          """, "https://example.com/sample");
        ViewModel.Url = fixture.Url;
        ViewModel.AudioOnly = false;
        ViewModel.VideoContainer = VideoContainer.Mp4;
        ViewModel.LoadMedia(fixture);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        if (ViewModel.VideoChoices.Count != 2 || !ViewModel.DownloadCommand.CanExecute(null))
            throw new InvalidOperationException("動画の画面状態が不正です。");
        var directory = Environment.GetEnvironmentVariable("YTDLPGUI_PREVIEW_DIR");
        void Render(string name)
        {
            if (directory is null) return;
            Directory.CreateDirectory(directory);
            using var bitmap = new RenderTargetBitmap(new((int)Bounds.Width, (int)Bounds.Height), new(96, 96));
            bitmap.Render(this);
            bitmap.Save(Path.Combine(directory, name));
        }
        Render("video.png");
        ViewModel.AudioOnly = true;
        ViewModel.AudioOutput = AudioOutput.Mp3;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        if (!ViewModel.ShowBitrate || !ViewModel.DownloadCommand.CanExecute(null))
            throw new InvalidOperationException("音声の画面状態が不正です。");
        Render("audio.png");
        ViewModel.Url = "https://example.com/changed";
        if (ViewModel.HasMedia || ViewModel.DownloadCommand.CanExecute(null))
            throw new InvalidOperationException("URL変更後に古い候補が残っています。");
        Console.WriteLine("PASS: native GUI, tools, video/audio and URL invalidation.");
    }
}
