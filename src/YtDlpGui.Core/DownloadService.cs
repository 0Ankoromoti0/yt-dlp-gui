using System.Globalization;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace YtDlpGui.Core;

public sealed record DownloadRequest(MediaInfo Media, bool AudioOnly, VideoContainer VideoContainer,
    VideoChoice? Video, AudioChoice? Audio, AudioOutput AudioOutput, int AudioBitrate, string Directory);
public sealed record DownloadEvent(string Stage, double? Percent = null, double? Speed = null,
    double? Eta = null, string? Log = null, string? FilePath = null);

public sealed class DownloadService(ProcessRunner runner)
{
    public static string ValidateUrl(string url)
    {
        url = url.Trim();
        if (url.Any(char.IsControl) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(uri.Host))
            throw new ArgumentException("http:// または https:// で始まる動画・音声のURLを入力してください。");
        return url;
    }

    public async Task<MediaInfo> AnalyzeAsync(string url, ToolPaths tools, IProgress<DownloadEvent> events, CancellationToken ct)
    {
        url = ValidateUrl(url);
        RequireEngine(tools);
        CheckYoutubeRuntime(url, tools);
        var args = Common(tools);
        args.AddRange(["--dump-single-json", "--skip-download", "--", url]);
        var result = await runner.RunAsync(tools.YtDlp!, args, ct,
            (line, error) => { if (error) events.Report(new("情報取得中", Log: line)); }, true);
        CheckResult(result);
        return MediaInfo.Parse(result.Output, url);
    }

    public async Task<string> DownloadAsync(DownloadRequest request, ToolPaths tools, IProgress<DownloadEvent> events, CancellationToken ct)
    {
        if (!tools.CanDownload) throw new InvalidOperationException("yt-dlp、FFmpeg、ffprobeをtoolsフォルダーに配置してください。");
        CheckYoutubeRuntime(request.Media.Url, tools);
        var directory = PrepareDirectory(request.Directory);
        // Converted audio is downloaded without re-encoding into an isolated
        // staging directory. We encode explicitly because yt-dlp copies matching
        // codecs and otherwise ignores a requested bitrate for those sources.
        var convertAudio = request.AudioOnly && request.AudioOutput != AudioOutput.Original;
        var staging = convertAudio ? Path.Combine(directory, ".ytdlp-gui", Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(request.Media.Url + "|" + request.Audio?.Selector + "|" + request.AudioOutput + "|" + request.AudioBitrate)))[..20]) : directory;
        Directory.CreateDirectory(staging);
        var args = BuildArguments(request with { Directory = staging }, tools);
        string? finalPath = null;
        var result = await runner.RunAsync(tools.YtDlp!, args, ct, (line, _) =>
        {
            var parsed = ParseEvent(line);
            if (parsed?.FilePath is { } path) finalPath = path;
            events.Report(parsed ?? new("", Log: line));
        });
        CheckResult(result);
        if (string.IsNullOrWhiteSpace(finalPath) || !File.Exists(finalPath))
            throw new IOException("取得ツールは終了しましたが、保存ファイルを確認できませんでした。詳細ログを確認してください。");
        return convertAudio ? await ConvertAudioAsync(finalPath, directory, request, tools, events, ct) : finalPath;
    }

    public static List<string> BuildArguments(DownloadRequest request, ToolPaths tools)
    {
        var args = Common(tools);
        args.AddRange(["--no-simulate", "--no-overwrites", "--windows-filenames", "--newline", "--progress", "--progress-delta", "0.2",
            "--progress-template", "download:__YTDLP_PROGRESS__%(progress)j",
            "--progress-template", "postprocess:__YTDLP_POST__%(progress)j",
            "--print", "after_move:__YTDLP_FILE__%(filepath)j",
            "--paths", request.Directory, "--output", "%(title).140s [%(id)s].%(ext)s"]);
        if (request.AudioOnly)
        {
            if (!FormatSelection.SupportsAudio(request.Media)) throw new InvalidOperationException("このURLには音声がありません。");
            args.AddRange(["--format", (request.Audio ?? new AudioChoice(null)).Selector, "--extract-audio",
                "--audio-format", "best"]);
            if (request.AudioOutput is AudioOutput.Mp3 or AudioOutput.M4a or AudioOutput.Opus)
            {
                if (request.AudioBitrate is not (64 or 96 or 128 or 160 or 192 or 256 or 320))
                    throw new ArgumentException("対応していない音声ビットレートです。");
            }
        }
        else
        {
            if (request.Video is null) throw new InvalidOperationException("取得する画質を選択してください。");
            var container = request.VideoContainer.ToString().ToLowerInvariant();
            args.AddRange(["--format", request.Video.Selector, "--merge-output-format", container, "--remux-video", container]);
        }
        args.AddRange(["--", ValidateUrl(request.Media.Url)]);
        return args;
    }

    private async Task<string> ConvertAudioAsync(string source, string directory, DownloadRequest request,
        ToolPaths tools, IProgress<DownloadEvent> events, CancellationToken ct)
    {
        var extension = request.AudioOutput.ToString().ToLowerInvariant();
        var output = Path.Combine(directory, Path.GetFileNameWithoutExtension(source) + "." + extension);
        if (File.Exists(output))
        {
            events.Report(new("", Log: "保存先に同名ファイルがあるため、既存ファイルを保持しました。"));
            CleanStaging(source);
            return output;
        }
        var temporary = Path.Combine(Path.GetDirectoryName(source)!, $"encoding-{Guid.NewGuid():N}.{extension}");
        try
        {
            events.Report(new("結合・変換中"));
            var args = BuildConversionArguments(source, temporary, request.AudioOutput, request.AudioBitrate);
            var result = await runner.RunAsync(tools.Ffmpeg!, args, ct,
                (line, _) => events.Report(new("", Log: line)));
            CheckResult(result);
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0)
                throw new IOException("音声の変換結果を確認できませんでした。");
            File.Move(temporary, output, overwrite: false);
            CleanStaging(source);
            return output;
        }
        finally
        {
            // Only remove the exact temporary file created by this conversion.
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static List<string> BuildConversionArguments(string source, string output, AudioOutput format, int bitrate)
    {
        var codec = format switch
        {
            AudioOutput.Mp3 => "libmp3lame", AudioOutput.M4a => "aac", AudioOutput.Opus => "libopus",
            AudioOutput.Flac => "flac", AudioOutput.Wav => "pcm_s16le",
            _ => throw new ArgumentException("この保存形式は変換を行いません。")
        };
        var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-i", source, "-map", "0:a:0", "-vn", "-c:a", codec };
        if (format is AudioOutput.Mp3 or AudioOutput.M4a or AudioOutput.Opus)
            args.AddRange(["-b:a", bitrate.ToString(CultureInfo.InvariantCulture) + "k"]);
        if (format == AudioOutput.Opus) args.AddRange(["-vbr", "off"]);
        args.AddRange(["-n", output]);
        return args;
    }

    private static void CleanStaging(string source)
    {
        try
        {
            File.Delete(source);
            // Non-recursive deletion refuses to delete any unexpected files.
            Directory.Delete(Path.GetDirectoryName(source)!, recursive: false);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static DownloadEvent? ParseEvent(string line)
    {
        try
        {
            if (line.StartsWith("__YTDLP_FILE__"))
                return new("保存を確認中", FilePath: JsonSerializer.Deserialize<string>(line[14..]));
            if (line.StartsWith("__YTDLP_POST__")) return new("結合・変換中");
            if (!line.StartsWith("__YTDLP_PROGRESS__")) return null;
            using var doc = JsonDocument.Parse(line[18..]);
            var progress = doc.RootElement;
            var downloaded = MediaInfo.Number(progress, "downloaded_bytes");
            var total = MediaInfo.Number(progress, "total_bytes") ?? MediaInfo.Number(progress, "total_bytes_estimate");
            var percent = downloaded is { } d && total is > 0 ? Math.Clamp(d / total.Value * 100, 0, 100) : (double?)null;
            return new("ダウンロード中", percent, MediaInfo.Number(progress, "speed"), MediaInfo.Number(progress, "eta"));
        }
        catch (JsonException) { return new("", Log: line); }
    }

    public static string PrepareDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("保存先のフォルダーを指定してください。");
        var path = Path.GetFullPath(directory.Trim());
        Directory.CreateDirectory(path);
        var probe = Path.Combine(path, $".ytdlp-gui-write-{Guid.NewGuid():N}.tmp");
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        return path;
    }

    private static List<string> Common(ToolPaths tools)
    {
        var args = new List<string> { "--ignore-config", "--no-playlist", "--no-colors", "--encoding", "utf-8", "--socket-timeout", "30" };
        if (tools.Ffmpeg is not null) args.AddRange(["--ffmpeg-location", Path.GetDirectoryName(tools.Ffmpeg)!]);
        if (tools.Deno is not null) args.AddRange(["--js-runtimes", $"deno:{tools.Deno}"]);
        return args;
    }

    private static void RequireEngine(ToolPaths tools)
    {
        if (tools.YtDlp is null) throw new InvalidOperationException($"{ToolPaths.ExecutableName("yt-dlp")}が見つかりません。toolsフォルダーに配置してください。");
    }
    private static void CheckYoutubeRuntime(string url, ToolPaths tools)
    {
        var host = new Uri(url).Host;
        if ((host == "youtu.be" || host == "youtube.com" || host.EndsWith(".youtube.com")) && tools.Deno is null)
            throw new InvalidOperationException($"YouTubeの取得にはDenoが必要です。{ToolPaths.ExecutableName("deno")}をtoolsフォルダーに配置してください。");
    }
    private static void CheckResult(ProcessResult result)
    {
        if (result.ExitCode != 0)
        {
            var error = result.Error.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault(line => line.StartsWith("ERROR:")) ?? result.Error.Trim();
            if (error.Length > 1400) error = error[^1400..];
            throw new InvalidOperationException("取得に失敗しました。URLや接続を確認し、情報を再取得してください。\n" + error);
        }
    }
}
