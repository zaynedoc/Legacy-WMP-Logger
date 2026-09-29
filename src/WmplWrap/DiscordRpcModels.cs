namespace WmplWrap;

/// <summary>
/// A read-only description of the item currently reported by Windows Media Player.
/// </summary>
public sealed record WmpPlaybackSnapshot(
    string Title,
    string Artist,
    string Album,
    string AlbumArtist,
    string SourceUrl,
    string Duration,
    double PositionSeconds,
    WmpPlaybackState State)
{
    public static WmpPlaybackSnapshot None { get; } = new("", "", "", "", "", "", 0, WmpPlaybackState.None);
    public bool HasMedia => (State is WmpPlaybackState.Playing or WmpPlaybackState.Paused) && !string.IsNullOrWhiteSpace(Title);
    public string EffectiveAlbumArtist => string.IsNullOrWhiteSpace(AlbumArtist) ? Artist : AlbumArtist;
    public string Identity => string.IsNullOrWhiteSpace(SourceUrl)
        ? $"{Title}\u001f{Artist}\u001f{Album}"
        : SourceUrl;
}

public enum WmpPlaybackState { None, Playing, Paused }

public sealed record DiscordAlbumArtMapping(string AlbumArtist, string AlbumTitle, string AssetKey)
{
    public bool Matches(WmpPlaybackSnapshot playback) =>
        Same(AlbumArtist, playback.EffectiveAlbumArtist) && Same(AlbumTitle, playback.Album);

    private static bool Same(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string value) => string.Join(' ', (value ?? "").Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

public sealed record DiscordRpcPreferences(
    bool Enabled = false,
    string ApplicationId = "1553580075688009838",
    bool DetectStalledPlayback = true,
    IReadOnlyList<DiscordAlbumArtMapping>? AlbumArtMappings = null,
    bool KeepPresenceBetweenTracks = true)
{
    public const string DefaultApplicationId = "1553580075688009838";
    public IReadOnlyList<DiscordAlbumArtMapping> Mappings => AlbumArtMappings ?? [];
}

public sealed record DiscordPresencePayload(
    string Details,
    string State,
    string LargeImageKey,
    string LargeImageText,
    string SmallImageKey,
    string SmallImageText,
    bool UsesFallbackArtwork,
    DateTimeOffset? ElapsedSinceUtc);

public static class DiscordPresenceFormatter
{
    public const string FallbackArtworkKey = "wmp_empty";
    public const string WmpIconKey = "wmp_icon";

    public static DiscordPresencePayload? Create(
        WmpPlaybackSnapshot playback,
        IReadOnlyList<DiscordAlbumArtMapping>? mappings,
        DateTimeOffset? elapsedSinceUtc = null)
    {
        if (!playback.HasMedia) return null;

        var mapping = (mappings ?? []).LastOrDefault(candidate => candidate.Matches(playback) && !string.IsNullOrWhiteSpace(candidate.AssetKey));
        var assetKey = mapping?.AssetKey.Trim() ?? FallbackArtworkKey;
        var title = Clip(string.IsNullOrWhiteSpace(playback.Title) ? "Unknown track" : playback.Title, 124);
        var artist = string.IsNullOrWhiteSpace(playback.Artist) ? "Unknown artist" : playback.Artist;
        var state = playback.State == WmpPlaybackState.Paused ? $"by {artist} \u00b7 Paused" : $"by {artist}";
        var albumText = string.IsNullOrWhiteSpace(playback.Album)
            ? playback.EffectiveAlbumArtist
            : string.IsNullOrWhiteSpace(playback.EffectiveAlbumArtist)
                ? playback.Album
                : $"{playback.Album} \u00b7 {playback.EffectiveAlbumArtist}";

        return new DiscordPresencePayload(
            $"\u201c{title}\u201d",
            Clip(state, 128),
            assetKey,
            Clip(string.IsNullOrWhiteSpace(albumText) ? "Windows Media Player" : albumText, 128),
            WmpIconKey,
            "Windows Media Player",
            mapping is null,
            playback.State == WmpPlaybackState.Playing ? elapsedSinceUtc : null);
    }

    private static string Clip(string value, int maximum) => value.Length <= maximum ? value : value[..Math.Max(1, maximum - 1)] + "\u2026";
}

/// <summary>
/// Converts WMP's current position into a stable Discord elapsed-time baseline. The Discord
/// client advances that clock itself, so Wrap only recalculates it after a track change or seek.
/// </summary>
public sealed class DiscordElapsedClock
{
    private const double SeekToleranceSeconds = 2;
    private string? _identity;
    private DateTimeOffset? _startedAtUtc;

    public DateTimeOffset? Observe(WmpPlaybackSnapshot playback, DateTimeOffset nowUtc)
    {
        if (!playback.HasMedia || playback.State != WmpPlaybackState.Playing)
        {
            Reset();
            return null;
        }

        var position = double.IsFinite(playback.PositionSeconds)
            ? Math.Max(0, playback.PositionSeconds)
            : 0;
        var requiresRebase = _startedAtUtc is null ||
            !string.Equals(_identity, playback.Identity, StringComparison.OrdinalIgnoreCase) ||
            Math.Abs((nowUtc - _startedAtUtc.Value).TotalSeconds - position) > SeekToleranceSeconds;

        if (requiresRebase)
        {
            _identity = playback.Identity;
            _startedAtUtc = nowUtc - TimeSpan.FromSeconds(position);
        }

        return _startedAtUtc;
    }

    public void Reset()
    {
        _identity = null;
        _startedAtUtc = null;
    }
}

/// <summary>
/// Keeps the last Discord activity visible across WMP's brief no-media interval while a
/// playlist advances. It deliberately preserves the existing activity rather than sending
/// a transient update that Discord may rate-limit.
/// </summary>
public sealed class DiscordPresenceHandoffBuffer
{
    public static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(5);
    private DateTimeOffset? _missingSinceUtc;

    public bool ShouldKeepLastPresence(WmpPlaybackSnapshot playback, bool enabled, DateTimeOffset nowUtc)
    {
        if (playback.HasMedia || !enabled)
        {
            Reset();
            return false;
        }

        _missingSinceUtc ??= nowUtc;
        return nowUtc - _missingSinceUtc.Value < GracePeriod;
    }

    public void Reset() => _missingSinceUtc = null;
}

/// <summary>
/// Turns repeated, unchanging playing samples into a paused state without changing raw WMP data.
/// </summary>
public sealed class WmpPlaybackStateTracker
{
    private WmpPlaybackSnapshot? _previous;
    private int _unchangedPlayingSamples;

    public WmpPlaybackSnapshot Observe(WmpPlaybackSnapshot current, bool detectStalledPlayback)
    {
        if (!current.HasMedia)
        {
            Reset();
            return current;
        }

        if (current.State == WmpPlaybackState.Paused)
        {
            _previous = current;
            _unchangedPlayingSamples = 0;
            return current;
        }

        if (detectStalledPlayback && _previous is { State: WmpPlaybackState.Playing } previous &&
            string.Equals(previous.Identity, current.Identity, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(previous.PositionSeconds - current.PositionSeconds) < 0.15)
        {
            _unchangedPlayingSamples++;
        }
        else
        {
            _unchangedPlayingSamples = 0;
        }

        _previous = current;
        return _unchangedPlayingSamples >= 3 ? current with { State = WmpPlaybackState.Paused } : current;
    }

    public void Reset()
    {
        _previous = null;
        _unchangedPlayingSamples = 0;
    }
}
