namespace YtDlpGui.Core;

public enum VideoContainer { Mp4, Mkv, Webm }
public enum AudioOutput { Original, Mp3, M4a, Opus, Flac, Wav }

public sealed record VideoChoice(MediaFormat Video, MediaFormat? Audio)
{
    public string Label => Video.VideoLabel + (HasAudio ? "" : " · 音声なし") +
        (Video.VideoCodec == "unknown" || Video.AudioCodec == "unknown" ? " · 互換性未確認" : "");
    public bool HasAudio => Video.HasAudio || Audio is not null;
    public string Selector => Audio is null ? Video.Id : $"{Video.Id}+{Audio.Id}";
    public long? EstimatedSize => Video.FileSize is { } v
        && (Audio is null || Audio.FileSize is not null) ? v + (Audio?.FileSize ?? 0) : null;
}

public sealed record AudioChoice(MediaFormat? Format)
{
    public string Label => Format?.AudioLabel ?? "自動 · 取得元の最高品質";
    public string Selector => Format?.Id ?? "bestaudio/best";
}

public static class FormatSelection
{
    public static IReadOnlyList<VideoChoice> Videos(MediaInfo media, VideoContainer container)
    {
        // yt-dlp's formats are sorted from worst to best. Preserve its audio
        // preference rather than comparing bitrates across unrelated codecs.
        var audio = media.Formats.LastOrDefault(f => !f.HasVideo && f.HasAudio && AudioFits(f, container));
        var hasAnyAudio = media.Formats.Any(f => f.HasAudio);
        return media.Formats.Where(f => f.HasVideo && VideoFits(f, container)
                && (!f.HasAudio || AudioFits(f, container)))
            .Select(f => new VideoChoice(f, f.HasAudio ? null : audio))
            .Where(c => c.HasAudio || !hasAnyAudio)
            .OrderByDescending(c => Math.Min(c.Video.Width ?? int.MaxValue, c.Video.Height ?? 0))
            .ThenByDescending(c => c.Video.Fps ?? 0)
            .ThenByDescending(c => media.Formats.ToList().IndexOf(c.Video))
            .ToArray();
    }

    public static IReadOnlyList<AudioChoice> Audios(MediaInfo media)
    {
        var standalone = media.Formats.Where(f => f.HasAudio && !f.HasVideo).Reverse().ToArray();
        var choices = standalone.Length > 0 ? standalone : media.Formats.Where(f => f.HasAudio).Reverse().ToArray();
        return new[] { new AudioChoice(null) }.Concat(choices.Select(f => new AudioChoice(f))).ToArray();
    }

    public static bool SupportsAudio(MediaInfo media) => media.Formats.Any(f => f.HasAudio);
    private static bool VideoFits(MediaFormat format, VideoContainer container) => container switch
    {
        VideoContainer.Mp4 => format.IsH264 || (format.VideoCodec == "unknown" && format.Extension == "mp4"),
        VideoContainer.Webm => format.IsWebmVideo || (format.VideoCodec == "unknown" && format.Extension == "webm"),
        _ => true
    };
    private static bool AudioFits(MediaFormat format, VideoContainer container) => container switch
    {
        VideoContainer.Mp4 => format.IsAac || (format.AudioCodec == "unknown" && format.Extension == "mp4"),
        VideoContainer.Webm => format.IsWebmAudio || (format.AudioCodec == "unknown" && format.Extension == "webm"),
        _ => true
    };
}
