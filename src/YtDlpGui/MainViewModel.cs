using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using YtDlpGui.Core;

namespace YtDlpGui;

public sealed record Option<T>(T Value, string Label);

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ProcessRunner _runner = new();
    private readonly SettingsStore _settings = new();
    private readonly DownloadService _service;
    private readonly IDesktopPlatform _platform;
    private CancellationTokenSource? _operation;
    private int _serial;
    private MediaInfo? _media;
    private ToolPaths _tools = new(null, null, null, null);
    private string _url = "", _directory = "", _status = "URLを入力して、情報を取得してください。";
    private string _error = "", _log = "", _lastFile = "", _toolSummary = "ツールを確認中…", _toolDetails = "";
    private string _title = "保存する動画・音声を選ぼう", _subtitle = "URLから取得できる品質を確認して、好きな形式で保存。";
    private string _speedText = "", _etaText = "", _stage = "準備完了";
    private bool _audioOnly, _busy, _ready, _indeterminate;
    private double _progress;
    private VideoContainer _container;
    private AudioOutput _audioOutput;
    private int _bitrate;
    private VideoChoice? _selectedVideo;
    private AudioChoice? _selectedAudio;
    private IReadOnlyList<VideoChoice> _videos = [];
    private IReadOnlyList<AudioChoice> _audios = [];
    private readonly Queue<string> _logLines = new();

    public MainViewModel(IDesktopPlatform? platform = null)
    {
        _platform = platform ?? new DesktopPlatform();
        _service = new(_runner);
        var settings = _settings.Load();
        _directory = settings.SaveDirectory;
        _container = settings.VideoContainer;
        _audioOutput = settings.AudioOutput;
        _bitrate = settings.AudioBitrate;
        _audioOnly = settings.AudioOnly;
        AnalyzeCommand = new AsyncCommand(AnalyzeAsync, () => !IsBusy && _ready && _tools.YtDlp is not null && !string.IsNullOrWhiteSpace(Url), ShowError);
        DownloadCommand = new AsyncCommand(DownloadAsync, () => !IsBusy && _ready && _tools.CanDownload && _media is not null
            && (!AudioOnly ? SelectedVideo is not null : FormatSelection.SupportsAudio(_media))
            && !string.IsNullOrWhiteSpace(SaveDirectory), ShowError);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy && _operation is not null);
        BrowseCommand = new AsyncCommand(BrowseAsync, () => !IsBusy, ShowError);
        OpenFolderCommand = new RelayCommand(OpenFolder, () => !IsBusy && Directory.Exists(SaveDirectory));
        UpdateCommand = new AsyncCommand(UpdateAsync, () => !IsBusy && _ready && IsLocalEngine, ShowError);
        RefreshToolsCommand = new AsyncCommand(InitializeAsync, () => !IsBusy, ShowError);
        RestoreCommand = new RelayCommand(RestoreEngine, () => !IsBusy && IsLocalEngine && File.Exists(_tools.YtDlp + ".previous"));
    }

    public Option<VideoContainer>[] VideoContainers { get; } = [new(VideoContainer.Mp4, "MP4 · 互換性優先"), new(VideoContainer.Mkv, "MKV · 元の品質を優先"), new(VideoContainer.Webm, "WebM")];
    public Option<AudioOutput>[] AudioOutputs { get; } = [new(AudioOutput.Original, "元の音声を保持"), new(AudioOutput.Mp3, "MP3"), new(AudioOutput.M4a, "M4A / AAC"), new(AudioOutput.Opus, "Opus"), new(AudioOutput.Flac, "FLAC"), new(AudioOutput.Wav, "WAV")];
    public int[] Bitrates { get; } = [64, 96, 128, 160, 192, 256, 320];
    public ICommand AnalyzeCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand BrowseCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand UpdateCommand { get; }
    public ICommand RefreshToolsCommand { get; }
    public ICommand RestoreCommand { get; }

    public string Url
    {
        get => _url;
        set
        {
            if (!Set(ref _url, value)) return;
            _media = null; _videos = []; _audios = []; _selectedVideo = null; _selectedAudio = null;
            Title = "保存する動画・音声を選ぼう";
            Subtitle = "情報取得ボタンで、このURLの保存候補を確認できます。";
            ErrorMessage = ""; Status = "情報を取得してください。"; Stage = "準備完了";
            ProgressValue = 0; LastFile = ""; SpeedText = ""; EtaText = "";
            Raise(nameof(VideoChoices), nameof(AudioChoices), nameof(SelectedVideo), nameof(SelectedAudio), nameof(HasMedia), nameof(NoVideoChoices), nameof(SelectionHint), nameof(OutputHint));
            Requery();
        }
    }
    public string SaveDirectory { get => _directory; set { if (Set(ref _directory, value)) Requery(); } }
    public bool AudioOnly
    {
        get => _audioOnly;
        set { if (Set(ref _audioOnly, value)) { Raise(nameof(VideoMode), nameof(NoVideoChoices), nameof(SelectionHint), nameof(OutputHint)); Requery(); } }
    }
    public bool VideoMode { get => !AudioOnly; set { if (value) AudioOnly = false; } }
    public VideoContainer VideoContainer
    {
        get => _container;
        set { if (Set(ref _container, value)) { RefreshChoices(); Raise(nameof(SelectionHint)); } }
    }
    public AudioOutput AudioOutput
    {
        get => _audioOutput;
        set { if (Set(ref _audioOutput, value)) Raise(nameof(ShowBitrate), nameof(OutputHint), nameof(AudioHint)); }
    }
    public int AudioBitrate { get => _bitrate; set => Set(ref _bitrate, value); }
    public IReadOnlyList<VideoChoice> VideoChoices => _videos;
    public IReadOnlyList<AudioChoice> AudioChoices => _audios;
    public VideoChoice? SelectedVideo
    {
        get => _selectedVideo;
        set { if (Set(ref _selectedVideo, value)) { Raise(nameof(OutputHint), nameof(SelectionHint)); Requery(); } }
    }
    public AudioChoice? SelectedAudio { get => _selectedAudio; set { if (Set(ref _selectedAudio, value)) Raise(nameof(OutputHint)); } }
    public bool HasMedia => _media is not null;
    public bool NoVideoChoices => HasMedia && VideoMode && _videos.Count == 0;
    public bool ShowBitrate => AudioOutput is AudioOutput.Mp3 or AudioOutput.M4a or AudioOutput.Opus;
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) { Raise(nameof(CanEdit)); Requery(); } } }
    public bool CanEdit => !IsBusy;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public string ErrorMessage { get => _error; private set { if (Set(ref _error, value)) Raise(nameof(HasError)); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Stage { get => _stage; private set => Set(ref _stage, value); }
    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }
    public string LogText { get => _log; private set => Set(ref _log, value); }
    public string ToolSummary { get => _toolSummary; private set => Set(ref _toolSummary, value); }
    public string ToolDetails { get => _toolDetails; private set => Set(ref _toolDetails, value); }
    public string SpeedText { get => _speedText; private set => Set(ref _speedText, value); }
    public string EtaText { get => _etaText; private set => Set(ref _etaText, value); }
    public double ProgressValue { get => _progress; private set { if (Set(ref _progress, value)) Raise(nameof(PercentText)); } }
    public bool IsIndeterminate { get => _indeterminate; private set { if (Set(ref _indeterminate, value)) Raise(nameof(PercentText)); } }
    public string PercentText => IsIndeterminate ? "処理中" : $"{ProgressValue:0}%";
    public string LastFile { get => _lastFile; private set { if (Set(ref _lastFile, value)) Raise(nameof(HasLastFile)); } }
    public bool HasLastFile => !string.IsNullOrWhiteSpace(LastFile);
    public string SelectionHint => _media is null ? "URLの情報を取得すると、選べる画質が表示されます。"
        : AudioOnly ? (FormatSelection.SupportsAudio(_media) ? "取得元の音声と、保存時の変換設定を選べます。" : "このURLには音声がありません。")
        : _videos.Count == 0 ? "この形式に対応する候補がありません。MKVなど別の形式を選んでください。"
        : VideoContainer == VideoContainer.Mp4 ? "H.264 / AACを優先。高解像度の候補が少ない場合はMKVを選択。"
        : SelectedVideo?.HasAudio == false ? "このURLは映像のみです。音声なしで保存します。"
        : "再エンコードせず、選択した映像と音声を保存します。";
    public string AudioHint => AudioOutput is AudioOutput.Flac or AudioOutput.Wav
        ? "FLAC / WAVに変換しても、元の圧縮音声から失われた情報は復元できません。"
        : AudioOutput == AudioOutput.Original ? "元のコーデックを保って音声を取り出します。不要な再エンコードを避けます。"
        : "変換時にビットレートを上げても、元の音声より音質は上がりません。";
    public string OutputHint
    {
        get
        {
            if (_media is null) return "出力名：動画タイトル [動画ID].拡張子";
            var extension = AudioOnly ? (AudioOutput == AudioOutput.Original ? "元音声に応じた拡張子" : AudioOutput.ToString().ToLowerInvariant())
                : VideoContainer.ToString().ToLowerInvariant();
            var size = !AudioOnly && SelectedVideo?.EstimatedSize is { } bytes ? $" · 約{bytes / 1024d / 1024d:0.#} MB" : "";
            return $"{_media.Title} [{_media.Id}].{extension}{size}";
        }
    }
    private bool IsLocalEngine => _platform.IsManagedEngine(_tools.YtDlp);

    public async Task InitializeAsync()
    {
        if (IsBusy) return;
        Begin("ツールを確認中");
        _ready = false;
        try
        {
            _tools = _platform.DiscoverTools();
            var details = await Task.WhenAll(new[] { ("yt-dlp", _tools.YtDlp), ("FFmpeg", _tools.Ffmpeg), ("ffprobe", _tools.Ffprobe), ("Deno", _tools.Deno) }
                .Select(async tool =>
                {
                    if (tool.Item2 is null) return $"{tool.Item1}: 見つかりません";
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_operation!.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    try
                    {
                        var result = await _runner.RunAsync(tool.Item2, [tool.Item1 is "FFmpeg" or "ffprobe" ? "-version" : "--version"], timeout.Token, captureOutput: true);
                        if (result.ExitCode != 0) return $"{tool.Item1}: 起動失敗\n{tool.Item2}";
                        return $"{tool.Item1}: {result.Output.Split('\n')[0].Trim()}\n{tool.Item2}";
                    }
                    catch (Exception ex) { return $"{tool.Item1}: 確認失敗 ({ex.Message})\n{tool.Item2}"; }
                }));
            ToolDetails = string.Join("\n\n", details);
            ToolSummary = _tools.CanDownload ? (_tools.Deno is null ? "取得ツール準備完了 · Denoなし" : "取得ツール準備完了") : "取得ツールが不足しています";
            _ready = true;
            if (!_tools.CanDownload) ErrorMessage = $"toolsフォルダーに{ToolPaths.ExecutableName("yt-dlp")} / {ToolPaths.ExecutableName("ffmpeg")} / {ToolPaths.ExecutableName("ffprobe")}を配置し、再確認してください。YouTube用に{ToolPaths.ExecutableName("deno")}も必要です。";
            Stage = "準備完了";
            Status = "URLを入力して、情報を取得してください。";
        }
        finally { End(); }
    }

    public async Task AnalyzeAsync()
    {
        Begin("情報取得中");
        _media = null; _videos = []; _audios = []; _selectedVideo = null; _selectedAudio = null;
        Raise(nameof(HasMedia), nameof(VideoChoices), nameof(AudioChoices), nameof(SelectedVideo), nameof(SelectedAudio), nameof(SelectionHint), nameof(OutputHint));
        Requery();
        try
        {
            var media = await _service.AnalyzeAsync(Url, _tools, Events(), _operation!.Token);
            LoadMedia(media);
            Stage = "選択できます";
            Status = AudioOnly && !FormatSelection.SupportsAudio(media) ? "音声がありません。動画モードを選択してください。" : "形式・品質・保存先を選んで、保存を開始してください。";
        }
        catch (OperationCanceledException) { Stage = "中断しました"; Status = "情報取得を中断しました。"; }
        catch (Exception ex) { ShowError(ex); }
        finally { End(); }
    }

    public void LoadMedia(MediaInfo media)
    {
        _media = media;
        Title = media.Title;
        Subtitle = $"{media.DurationLabel}" + (string.IsNullOrWhiteSpace(media.Uploader) ? "" : $"  ·  {media.Uploader}") + $"  ·  {media.Formats.Count}件の取得候補";
        _audios = FormatSelection.Audios(media); _selectedAudio = _audios.FirstOrDefault();
        RefreshChoices();
        Raise(nameof(HasMedia), nameof(AudioChoices), nameof(SelectedAudio), nameof(OutputHint));
    }

    public async Task DownloadAsync()
    {
        if (_media is null) return;
        Begin("ダウンロード中");
        LastFile = "";
        try
        {
            SaveSettings();
            var request = new DownloadRequest(_media, AudioOnly, VideoContainer, SelectedVideo, SelectedAudio, AudioOutput, AudioBitrate, SaveDirectory);
            LastFile = await _service.DownloadAsync(request, _tools, Events(), _operation!.Token);
            ProgressValue = 100; IsIndeterminate = false; Stage = "保存しました";
            Status = "完了しました。保存先を開いてファイルを確認できます。";
            SpeedText = ""; EtaText = "";
        }
        catch (OperationCanceledException) { Stage = "中断しました"; Status = "保存を中断しました。途中のデータは再取得時に利用できる場合があります。"; }
        catch (Exception ex) { ShowError(ex); }
        finally { End(); }
    }

    private void RefreshChoices()
    {
        _videos = _media is null ? [] : FormatSelection.Videos(_media, VideoContainer);
        _selectedVideo = _videos.FirstOrDefault();
        Raise(nameof(VideoChoices), nameof(SelectedVideo), nameof(NoVideoChoices), nameof(SelectionHint), nameof(OutputHint)); Requery();
    }
    private void Begin(string stage)
    {
        _serial++;
        _operation = new();
        IsBusy = true; ErrorMessage = ""; Stage = stage; Status = stage;
        ProgressValue = 0; IsIndeterminate = true; SpeedText = ""; EtaText = "";
        _logLines.Clear(); LogText = "";
    }
    private void End()
    {
        IsBusy = false; IsIndeterminate = false;
        _operation?.Dispose(); _operation = null;
        Requery();
    }
    private IProgress<DownloadEvent> Events()
    {
        var serial = _serial;
        return new Progress<DownloadEvent>(value =>
        {
            if (!IsBusy || serial != _serial) return;
            if (value.Log is not null) { AppendLog(value.Log); return; }
            if (!string.IsNullOrWhiteSpace(value.Stage)) { Stage = value.Stage; Status = value.Stage; }
            if (value.Stage == "結合・変換中") { IsIndeterminate = true; SpeedText = ""; EtaText = ""; }
            else if (value.Stage == "ダウンロード中")
            {
                IsIndeterminate = value.Percent is null;
                if (value.Percent is { } p) ProgressValue = p;
                SpeedText = value.Speed is > 0 ? $"{value.Speed / 1024d / 1024d:0.##} MB/s" : "";
                EtaText = value.Eta is >= 0 ? $"残り約 {TimeSpan.FromSeconds(Math.Min(value.Eta.Value, 864000)):g}" : "";
            }
        });
    }
    private void AppendLog(string line)
    {
        if (line.Length > 2000) line = line[..2000] + "…";
        _logLines.Enqueue(line); if (_logLines.Count > 250) _logLines.Dequeue();
        LogText = string.Join("\n", _logLines);
    }
    public void ShowError(Exception ex)
    {
        ErrorMessage = ex is UnauthorizedAccessException ? "保存先またはツールへのアクセス権がありません。別のフォルダーを指定してください。" : ex.Message;
        Stage = "確認が必要です"; Status = "処理を完了できませんでした。詳細を確認して、もう一度お試しください。";
        AppendLog(ex.ToString());
    }
    public void Cancel() { _operation?.Cancel(); Status = "中断しています…"; }
    public void SaveSettings()
    {
        try { _settings.Save(new() { SaveDirectory = SaveDirectory, VideoContainer = VideoContainer, AudioOutput = AudioOutput, AudioBitrate = AudioBitrate, AudioOnly = AudioOnly }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppendLog("設定を保存できませんでした: " + ex.Message); }
    }
    private async Task BrowseAsync()
    {
        if (await _platform.SelectFolderAsync(SaveDirectory) is { } folder) SaveDirectory = folder;
    }
    private void OpenFolder()
    {
        try
        {
            _platform.OpenFolder(SaveDirectory);
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private async Task UpdateAsync()
    {
        if (!IsLocalEngine) return;
        Begin("yt-dlpを更新中");
        var engine = _tools.YtDlp!;
        var backup = engine + ".previous";
        bool copied = false;
        try
        {
            File.Copy(engine, backup, true); copied = true;
            var result = await _runner.RunAsync(engine, ["--ignore-config", "--update", "--no-colors"], _operation!.Token,
                (line, _) => EventsForUpdate(line));
            if (result.ExitCode != 0) throw new IOException("更新に失敗しました。更新前のバージョンへ戻します。\n" + result.Error);
            var check = await _runner.RunAsync(engine, ["--version"], _operation.Token, captureOutput: true);
            if (check.ExitCode != 0) throw new IOException("更新後の起動確認に失敗しました。");
            ToolSummary = $"yt-dlp {check.Output.Trim()} · 更新済み";
            Stage = "更新しました"; Status = "yt-dlpを更新しました。URLの情報を再取得してください。";
        }
        catch (Exception ex)
        {
            if (copied) File.Copy(backup, engine, true);
            if (ex is OperationCanceledException) { Stage = "更新を中断しました"; Status = "更新前のバージョンを復元しました。"; }
            else ShowError(ex);
        }
        finally { End(); }
    }
    private void EventsForUpdate(string line)
    {
        _platform.Post(() => { if (IsBusy) AppendLog(line); });
    }
    private void RestoreEngine()
    {
        try
        {
            File.Copy(_tools.YtDlp + ".previous", _tools.YtDlp!, true);
            ToolSummary = "yt-dlpを前のバージョンへ復元しました";
            Status = "「再確認」でツールのバージョンを確認できます。";
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private static void Requery() => CommandRefresh.InvalidateRequerySuggested();
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; PropertyChanged?.Invoke(this, new(name)); return true;
    }
    private void Raise(params string[] names) { foreach (var name in names) PropertyChanged?.Invoke(this, new(name)); }
    public event PropertyChangedEventHandler? PropertyChanged;
}
