using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using YtDlpGui.Core;

if (args.FirstOrDefault() == "--sleep-child")
{
    var start = SelfStart("--grandchild");
    using var child = Process.Start(start)!;
    await File.WriteAllTextAsync(args[1], child.Id.ToString());
    for (int i = 0; i < 300; i++) { Console.WriteLine("出力 " + i); Console.Error.WriteLine("エラー " + i); }
    await Task.Delay(TimeSpan.FromMinutes(10));
    return;
}
if (args.FirstOrDefault() == "--grandchild") { await Task.Delay(TimeSpan.FromMinutes(10)); return; }

var workspace = Path.GetFullPath(args.FirstOrDefault() ?? ".");
var root = Path.Combine(workspace, "artifacts", "tests", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
Directory.CreateDirectory(root);
var results = new List<string>();
int failed = 0;
async Task Test(string name, Func<Task> action)
{
    try { await action(); results.Add("PASS " + name); Console.WriteLine(results[^1]); }
    catch (Exception ex) { failed++; results.Add("FAIL " + name + "\n" + ex); Console.WriteLine(results[^1]); }
}
Task Unit(Action action) { action(); return Task.CompletedTask; }
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}

var fixture = """
 {"id":"abc","title":"日本語の動画","duration":130,"formats":[
   {"format_id":"a-aac","url":"https://example.com/a","ext":"m4a","vcodec":"none","acodec":"mp4a.40.2","abr":192},
   {"format_id":"a-opus","url":"https://example.com/b","ext":"webm","vcodec":"none","acodec":"opus","abr":128},
   {"format_id":"720","url":"https://example.com/c","ext":"mp4","vcodec":"avc1.4d401f","acodec":"none","width":1280,"height":720,"fps":30},
   {"format_id":"1080","url":"https://example.com/d","ext":"mp4","vcodec":"avc1.64002a","acodec":"none","width":1920,"height":1080,"fps":60},
   {"format_id":"2160","url":"https://example.com/e","ext":"webm","vcodec":"vp9","acodec":"none","width":3840,"height":2160,"fps":30},
   {"format_id":"combined","url":"https://example.com/f","ext":"mp4","vcodec":"avc1","acodec":"mp4a.40.2","width":640,"height":360},
   {"format_id":"drm","url":"https://example.com/g","ext":"mp4","vcodec":"avc1","acodec":"mp4a","has_drm":true},
   {"format_id":"storyboard","url":"https://example.com/h","ext":"mhtml","vcodec":"none","acodec":"none"} ]}
 """;
var media = MediaInfo.Parse(fixture, "https://example.com/watch?a=1&b=2");
var fakeTools = new ToolPaths("yt-dlp.exe", @"C:\tools\ffmpeg.exe", @"C:\tools\ffprobe.exe", @"C:\tools\deno.exe");
await Test("Bundled native tool names take precedence over PATH", () => Unit(() =>
{
    var bundle = Path.Combine(root, "bundle");
    Directory.CreateDirectory(Path.Combine(bundle, "tools"));
    foreach (var name in new[] { "yt-dlp", "ffmpeg", "ffprobe", "deno" })
        File.WriteAllText(Path.Combine(bundle, "tools", ToolPaths.ExecutableName(name)), "fixture");
    var discovered = ToolPaths.Discover(bundle);
    Assert(discovered.CanDownload && discovered.Deno is not null, "native tool names not discovered");
    Assert(discovered.YtDlp == Path.Combine(bundle, "tools", ToolPaths.ExecutableName("yt-dlp")), "bundled tool not preferred");
}));
await Test("Shared screen state preserves choices, folder actions and URL invalidation", () => Unit(() =>
{
    var previousSettings = Environment.GetEnvironmentVariable("YTDLPGUI_SETTINGS_DIR");
    Environment.SetEnvironmentVariable("YTDLPGUI_SETTINGS_DIR", Path.Combine(root, "viewmodel-settings"));
    try
    {
        var platform = new YtDlpGui.DesktopPlatform { FolderToSelect = Path.Combine(root, "保存先 空白") };
        Directory.CreateDirectory(platform.FolderToSelect);
        var vm = new YtDlpGui.MainViewModel(platform) { Url = media.Url };
        vm.LoadMedia(media);
        Assert(vm.VideoChoices.Count == 3 && vm.SelectedVideo?.Selector == "1080+a-aac", "shared format selection changed");
        vm.AudioOnly = true; vm.AudioOutput = AudioOutput.Mp3; vm.AudioBitrate = 320;
        Assert(vm.ShowBitrate, "MP3 bitrate hidden");
        vm.BrowseCommand.Execute(null);
        Assert(vm.SaveDirectory == platform.FolderToSelect, "native picker selection not applied");
        vm.OpenFolderCommand.Execute(null);
        Assert(platform.OpenedFolder == vm.SaveDirectory, "open-folder action not delegated");
        vm.SaveSettings();
        var restored = new YtDlpGui.MainViewModel(platform);
        Assert(restored.AudioOnly && restored.AudioOutput == AudioOutput.Mp3 && restored.AudioBitrate == 320
            && restored.SaveDirectory == vm.SaveDirectory, "screen settings not restored");
        vm.Url = "https://example.com/changed";
        Assert(!vm.HasMedia && vm.SelectedVideo is null && vm.VideoChoices.Count == 0 && !vm.DownloadCommand.CanExecute(null), "stale selection survived URL change");
    }
    finally { Environment.SetEnvironmentVariable("YTDLPGUI_SETTINGS_DIR", previousSettings); }
}));
var videoChoices = FormatSelection.Videos(media, VideoContainer.Mp4);
await Test("MP4 filters incompatible codecs and combines AAC", () => Unit(() =>
{
    Assert(videoChoices.Count == 3, "unexpected video count");
    Assert(videoChoices[0].Selector == "1080+a-aac", "highest H.264 with AAC required");
    Assert(!media.Formats.Any(f => f.Id is "drm" or "storyboard"), "non-media formats present");
}));
await Test("MKV exposes 4K and honors engine audio preference", () => Unit(() =>
    Assert(FormatSelection.Videos(media, VideoContainer.Mkv)[0].Selector == "2160+a-opus", "codec preference lost")));
await Test("WebM uses compatible audio and video", () => Unit(() =>
    Assert(FormatSelection.Videos(media, VideoContainer.Webm).Single().Selector == "2160+a-opus", "bad WebM pair")));
await Test("Combined source does not duplicate audio", () => Unit(() =>
    Assert(videoChoices.Last().Selector == "combined", "duplicate audio")));
await Test("No compatible audio prevents silent fallback", () => Unit(() =>
{
    var incompatible = media with { Formats = media.Formats.Where(f => f.Id is "1080" or "a-opus").ToArray() };
    Assert(FormatSelection.Videos(incompatible, VideoContainer.Mp4).Count == 0, "silent video exposed");
}));
await Test("Video-only media is marked as silent", () => Unit(() =>
{
    var silent = media with { Formats = media.Formats.Where(f => f.Id == "1080").ToArray() };
    Assert(!FormatSelection.Videos(silent, VideoContainer.Mp4).Single().HasAudio, "silent source claims audio");
    Assert(!FormatSelection.SupportsAudio(silent), "silent audio mode enabled");
}));
await Test("Missing metadata and direct URLs remain usable", () => Unit(() =>
{
    var unknown = MediaInfo.Parse("""{"id":"direct","formats":[{"format_id":"0","ext":"mp4","url":"https://example.com/x.mp4"}]}""", "https://example.com/x.mp4");
    Assert(FormatSelection.Videos(unknown, VideoContainer.Mp4).Single().Label.Contains("互換性未確認"), "unknown codec falsely guaranteed");
    Assert(unknown.DurationLabel == "長さ不明", "missing duration failed");
}));
await Test("Playlist and live media are rejected", () => Unit(() =>
{
    Throws<InvalidOperationException>(() => MediaInfo.Parse("""{"_type":"playlist","entries":[]}""", "https://example.com"));
    Throws<InvalidOperationException>(() => MediaInfo.Parse("""{"is_live":true}""", "https://example.com"));
}));
await Test("URLs are passed as arguments without shell interpolation", () => Unit(() =>
{
    Throws<ArgumentException>(() => DownloadService.ValidateUrl("file:///C:/test.mp4"));
    Throws<ArgumentException>(() => DownloadService.ValidateUrl("https://example.com/\n--exec"));
    var request = new DownloadRequest(media, false, VideoContainer.Mp4, videoChoices[0], null, AudioOutput.Original, 192, Path.Combine(root, "日本語 空白"));
    var command = DownloadService.BuildArguments(request, fakeTools);
    Assert(command[^2] == "--" && command[^1] == media.Url, "URL delimiter missing");
    Assert(command.Contains(request.Directory), "path split into words");
    Assert(command.Contains("--remux-video") && !command.Contains("--recode-video"), "unexpected re-encode");
}));
await Test("Lossless and original outputs do not specify bitrate", () => Unit(() =>
{
    foreach (var output in new[] { AudioOutput.Original, AudioOutput.Wav, AudioOutput.Flac })
    {
        var request = new DownloadRequest(media, true, VideoContainer.Mp4, null, null, output, 192, root);
        Assert(!DownloadService.BuildArguments(request, fakeTools).Contains("--audio-quality"), "bitrate used for lossless output");
    }
}));
await Test("Progress protocol handles totals, unknown values, and escaped paths", () => Unit(() =>
{
    var progress = DownloadService.ParseEvent("__YTDLP_PROGRESS__{\"downloaded_bytes\":25,\"total_bytes\":100,\"speed\":null,\"eta\":3}");
    Assert(progress?.Percent == 25 && progress.Speed is null, "progress parse failed");
    Assert(DownloadService.ParseEvent("__YTDLP_PROGRESS__{\"total_bytes\":null}")?.Percent is null, "unknown total invented");
    var path = Path.Combine(root, "日本語 \"test\".mp4");
    Assert(DownloadService.ParseEvent("__YTDLP_FILE__" + JsonSerializer.Serialize(path))?.FilePath == path, "path escaping failed");
    Assert(DownloadService.ParseEvent("__YTDLP_POST__{}")?.Stage == "結合・変換中", "postprocess stage missing");
}));
await Test("Settings persist and recover from corrupt files", () => Unit(() =>
{
    var store = new SettingsStore(Path.Combine(root, "settings"));
    store.Save(new() { SaveDirectory = root, AudioBitrate = 320, AudioOnly = true });
    Assert(store.Load().AudioBitrate == 320 && store.Load().AudioOnly, "settings lost");
    File.WriteAllText(Path.Combine(root, "settings", "settings.json"), "invalid json");
    Assert(store.Load().AudioBitrate == 192, "invalid settings not recovered");
}));
var runner = new ProcessRunner();
await Test("Cancellation terminates the process tree and drains both pipes", async () =>
{
    var pidFile = Path.Combine(root, "grandchild.pid");
    using var cts = new CancellationTokenSource();
    var start = SelfStart("--sleep-child", pidFile);
    var task = runner.RunAsync(start.FileName, start.ArgumentList, cts.Token, captureOutput: true);
    var timer = Stopwatch.StartNew();
    while (!File.Exists(pidFile) && timer.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(50);
    Assert(File.Exists(pidFile), "child failed to start");
    var childId = int.Parse(await File.ReadAllTextAsync(pidFile));
    cts.Cancel();
    try { await task.WaitAsync(TimeSpan.FromSeconds(10)); throw new Exception("cancellation not reported"); }
    catch (OperationCanceledException) { }
    try { using var process = Process.GetProcessById(childId); Assert(process.HasExited, "grandchild survived"); }
    catch (ArgumentException) { }
});

if (args.Contains("--integration"))
{
    var tools = ToolPaths.Discover(Path.Combine(workspace, "artifacts", "YtDlpGui"));
    Assert(tools.CanDownload, "yt-dlp / ffmpeg / ffprobe required for integration tests");
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    var ct = timeout.Token;
    var sample = Path.Combine(root, "sample.mp4");
    var generated = await runner.RunAsync(tools.Ffmpeg!, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=24", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-t", "3", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", sample], ct);
    Assert(generated.ExitCode == 0, generated.Error);
    var audioFile = Path.Combine(root, "source.m4a");
    generated = await runner.RunAsync(tools.Ffmpeg!, ["-hide_banner", "-loglevel", "error", "-i", sample, "-vn", "-c:a", "copy", audioFile], ct);
    Assert(generated.ExitCode == 0, generated.Error);
    var dashDirectory = Path.Combine(root, "dash"); Directory.CreateDirectory(dashDirectory);
    generated = await runner.RunAsync(tools.Ffmpeg!, ["-hide_banner", "-loglevel", "error", "-i", sample, "-map", "0:v", "-map", "0:v", "-map", "0:a", "-filter:v:0", "scale=640:360", "-filter:v:1", "scale=1280:720", "-c:v", "libx264", "-preset", "ultrafast", "-g", "24", "-sc_threshold", "0", "-b:v:0", "600k", "-b:v:1", "1800k", "-c:a", "aac", "-b:a", "128k", "-f", "dash", "-seg_duration", "1", "-adaptation_sets", "id=0,streams=v id=1,streams=a", "manifest.mpd"], ct, workingDirectory: dashDirectory);
    Assert(generated.ExitCode == 0, generated.Error);
    await using var server = new MediaServer(root);
    var service = new DownloadService(runner);
    var events = new List<DownloadEvent>();
    var progress = new InlineProgress(value => { lock (events) events.Add(value); });
    MediaInfo? direct = null;
    await Test("Real yt-dlp analyzes a direct media URL", async () =>
    {
        direct = await service.AnalyzeAsync(server.Url + "sample.mp4", tools, progress, ct);
        Assert(FormatSelection.Videos(direct, VideoContainer.Mp4).Count > 0, "direct URL has no choices");
    });
    async Task<JsonDocument> Probe(string file)
    {
        var probe = await runner.RunAsync(tools.Ffprobe!, ["-v", "error", "-show_streams", "-show_format", "-of", "json", file], ct, captureOutput: true);
        Assert(probe.ExitCode == 0, probe.Error);
        return JsonDocument.Parse(probe.Output);
    }
    await Test("Real MP4 download preserves video and audio in a Japanese path", async () =>
    {
        Assert(direct is not null, "analysis failed");
        var output = await service.DownloadAsync(new(direct!, false, VideoContainer.Mp4,
            FormatSelection.Videos(direct!, VideoContainer.Mp4)[0], null, AudioOutput.Original, 192, Path.Combine(root, "保存先 空白", "mp4")), tools, progress, ct);
        Assert(Path.GetExtension(output) == ".mp4", "wrong extension");
        using var probe = await Probe(output);
        var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        Assert(streams.Any(s => s.GetProperty("codec_type").GetString() == "video") && streams.Any(s => s.GetProperty("codec_type").GetString() == "audio"), "missing stream");
        Assert(events.Any(e => e.Percent is not null), "structured progress not emitted");
    });
    foreach (var (audioOutput, bitrate, expectedCodec) in new[] { (AudioOutput.Mp3, 128, "mp3"), (AudioOutput.Mp3, 320, "mp3"), (AudioOutput.M4a, 192, "aac"), (AudioOutput.Opus, 128, "opus"), (AudioOutput.Flac, 192, "flac"), (AudioOutput.Wav, 192, "pcm_s16le"), (AudioOutput.Original, 192, "aac") })
    {
        await Test($"Real audio output {audioOutput} / {bitrate}", async () =>
        {
            Assert(direct is not null, "analysis failed");
            var output = await service.DownloadAsync(new(direct!, true, VideoContainer.Mp4, null, new AudioChoice(null), audioOutput, bitrate,
                Path.Combine(root, "audio", audioOutput + "-" + bitrate)), tools, progress, ct);
            using var probe = await Probe(output);
            var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            Assert(streams.Length == 1 && streams[0].GetProperty("codec_type").GetString() == "audio", "audio output contains video");
            Assert(streams[0].GetProperty("codec_name").GetString() == expectedCodec, "wrong audio codec");
            if (audioOutput == AudioOutput.Mp3)
                Assert(Math.Abs(int.Parse(streams[0].GetProperty("bit_rate").GetString()!) - bitrate * 1000) < 2000, "bitrate setting ignored");
        });
    }
    await Test("Real standalone audio URL works in audio mode", async () =>
    {
        var info = await service.AnalyzeAsync(server.Url + "source.m4a", tools, progress, ct);
        Assert(FormatSelection.SupportsAudio(info) && FormatSelection.Videos(info, VideoContainer.Mp4).Count == 0, "wrong media kind");
        var output = await service.DownloadAsync(new(info, true, VideoContainer.Mp4, null, null, AudioOutput.Original, 192, Path.Combine(root, "standalone")), tools, progress, ct);
        Assert(File.Exists(output), "standalone output missing");
    });
    await Test("Bitrate is enforced even when source and output codecs match", async () =>
    {
        var info = await service.AnalyzeAsync(server.Url + "source.m4a", tools, progress, ct);
        var output = await service.DownloadAsync(new(info, true, VideoContainer.Mp4, null, null, AudioOutput.M4a, 64, Path.Combine(root, "same-codec")), tools, progress, ct);
        using var probe = await Probe(output);
        var audio = probe.RootElement.GetProperty("streams")[0];
        var bitrate = int.Parse(audio.GetProperty("bit_rate").GetString()!);
        Assert(bitrate is > 48000 and < 80000, "matching AAC codec skipped requested conversion: " + bitrate);
        Assert(!Directory.EnumerateFiles(Path.Combine(root, "same-codec", ".ytdlp-gui"), "*", SearchOption.AllDirectories).Any(), "successful staging left media behind");
    });
    await Test("DASH quality selection downloads and merges the chosen resolution", async () =>
    {
        var info = await service.AnalyzeAsync(server.Url + "dash/manifest.mpd", tools, progress, ct);
        var choices = FormatSelection.Videos(info, VideoContainer.Mp4);
        Assert(choices.Any(c => c.Video.Height == 360) && choices.Any(c => c.Video.Height == 720), "resolution choices missing");
        foreach (var height in new[] { 360, 720 })
        {
            var choice = choices.First(c => c.Video.Height == height);
            Assert(choice.Audio is not null, "separate audio not selected");
            var output = await service.DownloadAsync(new(info, false, VideoContainer.Mp4, choice, null, AudioOutput.Original, 192, Path.Combine(root, "dash-output", height.ToString())), tools, progress, ct);
            using var probe = await Probe(output);
            var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            Assert(streams.First(s => s.GetProperty("codec_type").GetString() == "video").GetProperty("height").GetInt32() == height, "wrong resolution");
            Assert(streams.Any(s => s.GetProperty("codec_type").GetString() == "audio"), "merged file silent");
        }
    });
    await Test("Real MKV remux and existing-file protection", async () =>
    {
        Assert(direct is not null, "analysis failed");
        var request = new DownloadRequest(direct!, false, VideoContainer.Mkv, FormatSelection.Videos(direct!, VideoContainer.Mkv)[0], null, AudioOutput.Original, 192, Path.Combine(root, "mkv"));
        var output = await service.DownloadAsync(request, tools, progress, ct);
        Assert(Path.GetExtension(output) == ".mkv", "remux failed");
        var before = File.GetLastWriteTimeUtc(output);
        await service.DownloadAsync(request, tools, progress, ct);
        Assert(File.GetLastWriteTimeUtc(output) == before, "existing file overwritten");
    });
    await Test("Invalid media URL reports an error", async () =>
    {
        try { await service.AnalyzeAsync(server.Url + "missing.mp4", tools, progress, ct); }
        catch (InvalidOperationException) { return; }
        throw new Exception("404 treated as success");
    });
    await File.WriteAllTextAsync(Path.Combine(root, "events.log"), string.Join("\n", events.Select(e => e.Log ?? e.Stage + " " + e.Percent)));
}
await File.WriteAllTextAsync(Path.Combine(root, "results.txt"), string.Join("\n", results) + $"\n\n{results.Count - failed} passed, {failed} failed.");
Console.WriteLine($"RESULT: {results.Count - failed} passed, {failed} failed. {root}");
Environment.ExitCode = failed == 0 ? 0 : 1;

static ProcessStartInfo SelfStart(params string[] arguments)
{
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
    if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    foreach (var arg in arguments) start.ArgumentList.Add(arg);
    return start;
}

sealed class InlineProgress(Action<DownloadEvent> action) : IProgress<DownloadEvent>
{ public void Report(DownloadEvent value) => action(value); }

sealed class MediaServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly string _root;
    private readonly Task _loop;
    private readonly List<Task> _connections = [];
    public string Url { get; }
    public MediaServer(string root)
    {
        _root = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        _listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
        _loop = AcceptAsync();
    }
    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _connections.Add(HandleAsync(client));
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }
    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var first = (await reader.ReadLineAsync())?.Split(' ');
                if (first is null || first.Length < 2) return;
                string? range = null;
                while (await reader.ReadLineAsync() is { Length: > 0 } header)
                    if (header.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)) range = header[6..].Trim();
                var relative = Uri.UnescapeDataString(first[1].Split('?')[0]).TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var file = Path.GetFullPath(Path.Combine(_root, relative));
                if (!file.StartsWith(_root, StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n")); return;
                }
                var data = await File.ReadAllBytesAsync(file);
                long start = 0, end = data.Length - 1;
                bool partial = range?.StartsWith("bytes=") == true;
                if (partial)
                {
                    var parts = range![6..].Split('-');
                    if (long.TryParse(parts[0], out var parsedStart)) start = parsedStart;
                    if (parts.Length > 1 && long.TryParse(parts[1], out var parsedEnd)) end = Math.Min(end, parsedEnd);
                }
                start = Math.Clamp(start, 0, data.Length - 1); end = Math.Clamp(end, start, data.Length - 1);
                var type = Path.GetExtension(file) switch { ".mp4" => "video/mp4", ".m4a" => "audio/mp4", ".mpd" => "application/dash+xml", _ => "application/octet-stream" };
                var headers = $"HTTP/1.1 {(partial ? "206 Partial Content" : "200 OK")}\r\nContent-Type: {type}\r\nContent-Length: {end - start + 1}\r\nAccept-Ranges: bytes\r\nConnection: close\r\n";
                if (partial) headers += $"Content-Range: bytes {start}-{end}/{data.Length}\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(headers + "\r\n"));
                if (first[0] != "HEAD") await stream.WriteAsync(data.AsMemory((int)start, (int)(end - start + 1)));
            }
            catch (IOException) { }
        }
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _listener.Stop(); await _loop; await Task.WhenAll(_connections); _stop.Dispose();
    }
}
