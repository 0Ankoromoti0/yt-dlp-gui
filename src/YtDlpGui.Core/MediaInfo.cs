using System.Globalization;
using System.Text.Json;

namespace YtDlpGui.Core;

public sealed record MediaFormat(
    string Id, string Extension, string VideoCodec, string AudioCodec,
    int? Width, int? Height, double? Fps, double? AudioBitrate, int? SampleRate,
    int? Channels, long? FileSize, string? Language, string? DynamicRange, double? Preference)
{
    public bool HasVideo => VideoCodec is not ("none" or "");
    public bool HasAudio => AudioCodec is not ("none" or "");
    public bool IsH264 => VideoCodec.StartsWith("avc", StringComparison.OrdinalIgnoreCase)
        || VideoCodec.StartsWith("h264", StringComparison.OrdinalIgnoreCase);
    public bool IsAac => AudioCodec.StartsWith("mp4a", StringComparison.OrdinalIgnoreCase)
        || AudioCodec.StartsWith("aac", StringComparison.OrdinalIgnoreCase);
    public bool IsWebmVideo => new[] { "vp8", "vp9", "vp09", "av01", "av1" }
        .Any(prefix => VideoCodec.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    public bool IsWebmAudio => AudioCodec is "opus" or "vorbis";
    public string AudioLabel => $"{AudioCodec.ToUpperInvariant()} · {Number(AudioBitrate, " kbps")} · {Number(SampleRate / 1000d, " kHz")}" +
        (Channels is null ? "" : $" · {Channels}ch") + (string.IsNullOrWhiteSpace(Language) ? "" : $" · {Language}") + $"  [{Id}]";
    public string VideoLabel => $"{(Height is null ? "解像度不明" : $"{Width}×{Height}")} · {Number(Fps, " fps")} · {VideoCodec.ToUpperInvariant()}" +
        (DynamicRange is null or "SDR" ? "" : $" · {DynamicRange}") + $"  [{Id}]";
    private static string Number(double? value, string unit) => value is > 0 ? value.Value.ToString("0.#", CultureInfo.InvariantCulture) + unit : "不明";
}

public sealed record MediaInfo(string Id, string Title, string Url, double? Duration, string? Uploader,
    IReadOnlyList<MediaFormat> Formats, bool IsLive)
{
    public string DurationLabel => Duration is > 0
        ? TimeSpan.FromSeconds(Duration.Value).ToString(Duration >= 3600 ? @"h\:mm\:ss" : @"m\:ss") : "長さ不明";

    public static MediaInfo Parse(string json, string inputUrl)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (Text(root, "_type") is "playlist" or "multi_video" || root.TryGetProperty("entries", out _))
            throw new InvalidOperationException("初期版は動画・音声1件ずつの保存に対応しています。個別のURLを入力してください。");
        var live = Text(root, "live_status");
        if (Flag(root, "is_live") || live is "is_live" or "is_upcoming")
            throw new InvalidOperationException("ライブ配信中・配信予定のURLには対応していません。配信終了後の動画を指定してください。");
        var formats = new List<MediaFormat>();
        if (root.TryGetProperty("formats", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var item in list.EnumerateArray()) AddFormat(item, formats);
        else AddFormat(root, formats);
        if (formats.Count == 0) throw new InvalidOperationException("取得できる動画・音声が見つかりませんでした。");
        return new(Text(root, "id") ?? "media", Text(root, "title") ?? "タイトル不明", inputUrl,
            Number(root, "duration"), Text(root, "uploader"), formats, false);
    }

    private static void AddFormat(JsonElement item, List<MediaFormat> formats)
    {
        if (Flag(item, "has_drm")) return;
        var id = Text(item, "format_id");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(Text(item, "url"))) return;
        var vcodec = Text(item, "vcodec") ?? "";
        var acodec = Text(item, "acodec") ?? "";
        // Unknown codecs cannot be assumed to be compatible. MKV can expose
        // a stream whose type is indicated by height or explicit audio-only data.
        if (vcodec == "" && Number(item, "height") is > 0) vcodec = "unknown";
        if (acodec == "" && (Number(item, "abr") is > 0 || vcodec == "none")) acodec = "unknown";
        var ext = Text(item, "ext") ?? "";
        if (vcodec == "" && acodec == "")
        {
            if (ext is "mp4" or "mkv" or "webm" or "mov" or "avi" or "flv")
                (vcodec, acodec) = ("unknown", "unknown");
            else if (ext is "mp3" or "m4a" or "aac" or "opus" or "ogg" or "wav" or "flac")
                (vcodec, acodec) = ("none", "unknown");
        }
        var format = new MediaFormat(id, Text(item, "ext") ?? "", vcodec, acodec,
            Integer(item, "width"), Integer(item, "height"), Number(item, "fps"),
            Number(item, "abr"), Integer(item, "asr"), Integer(item, "audio_channels"),
            Long(item, "filesize") ?? Long(item, "filesize_approx"), Text(item, "language"),
            Text(item, "dynamic_range"), Number(item, "preference"));
        if (format.HasAudio || format.HasVideo) formats.Add(format);
    }

    internal static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    internal static double? Number(JsonElement element, string name) => element.TryGetProperty(name, out var value)
        && value.TryGetNumber(out var number) ? number : null;
    private static int? Integer(JsonElement element, string name) => Number(element, name) is { } n ? (int)n : null;
    private static long? Long(JsonElement element, string name) => Number(element, name) is { } n ? (long)n : null;
    internal static bool Flag(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

internal static class JsonNumbers
{
    public static bool TryGetNumber(this JsonElement value, out double number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number) && double.IsFinite(number);
    }
}
