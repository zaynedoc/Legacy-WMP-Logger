using DiscordRPC;

namespace WmplWrap.Desktop;

internal sealed record DiscordRpcStatus(
    string Summary,
    string NowPlaying,
    string Artwork,
    string LastError,
    DateTimeOffset? LastUpdatedAtUtc);

/// <summary>
/// Owns the local Discord IPC connection and polling loop. It never contacts a Wrap service.
/// </summary>
internal sealed class DiscordPresenceService : IDisposable
{
    private readonly WmpPlaybackBridge _playbackBridge = new();
    private readonly SemaphoreSlim _tickGate = new(1, 1);
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private Task? _monitor;
    private DiscordRpcClient? _client;
    private DiscordRpcPreferences _preferences = new();
    private WmpPlaybackStateTracker _stateTracker = new();
    private DiscordElapsedClock _elapsedClock = new();
    private DiscordPresenceHandoffBuffer _handoffBuffer = new();
    private DiscordPresencePayload? _lastPayload;
    private WmpPlaybackSnapshot? _currentPlayback;
    private bool _disposed;

    public event Action<DiscordRpcStatus>? StatusChanged;
    public WmpPlaybackSnapshot? CurrentPlayback { get { lock (_gate) return _currentPlayback; } }

    public void Start(DiscordRpcPreferences preferences)
    {
        StopCore(false);
        _preferences = preferences;
        _stateTracker = new WmpPlaybackStateTracker();
        _elapsedClock = new DiscordElapsedClock();
        _handoffBuffer = new DiscordPresenceHandoffBuffer();
        _lastPayload = null;
        _cancellation = new CancellationTokenSource();

        try
        {
            var client = new DiscordRpcClient(preferences.ApplicationId);
            var initialized = client.Initialize();
            _client = client;
            Publish(initialized
                ? new DiscordRpcStatus("Discord connected; waiting for Windows Media Player playback", "", "", "", null)
                : new DiscordRpcStatus("Discord desktop is unavailable; start Discord and reconnect", "", "", "", null));

            if (initialized)
                _monitor = Task.Run(() => MonitorAsync(_cancellation.Token));
        }
        catch (Exception error)
        {
            _client?.Dispose();
            _client = null;
            Publish(new DiscordRpcStatus("Discord Rich Presence could not start", "", "", FriendlyError(error), null));
        }
    }

    public async Task RefreshAsync()
    {
        if (_client is null)
        {
            Publish(new DiscordRpcStatus("Discord Rich Presence is not connected", "", "", "Use Save & reconnect after Discord is running.", null));
            return;
        }

        await TickAsync(CancellationToken.None);
    }

    public void Reconnect() => Start(_preferences);

    public void Stop() => StopCore(true);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopCore(false);
        _playbackBridge.Dispose();
        _tickGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await TickAsync(cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        if (!await _tickGate.WaitAsync(0, cancellationToken)) return;
        try
        {
            var playback = await _playbackBridge.ReadAsync(cancellationToken);
            playback = _stateTracker.Observe(playback, _preferences.DetectStalledPlayback);
            lock (_gate) _currentPlayback = playback.HasMedia ? playback : null;

            var now = DateTimeOffset.UtcNow;
            if (!playback.HasMedia)
            {
                if (_lastPayload is { } lastPayload &&
                    _handoffBuffer.ShouldKeepLastPresence(playback, _preferences.KeepPresenceBetweenTracks, now))
                {
                    Publish(new DiscordRpcStatus(
                        "Holding the last Discord status while Windows Media Player changes tracks",
                        $"{lastPayload.Details} \u2014 {lastPayload.State}",
                        "Will clear after 5 seconds without playback",
                        "",
                        null));
                    return;
                }

                _elapsedClock.Reset();
                ClearPresence();
                var bridgeError = _playbackBridge.LastError;
                var summary = string.IsNullOrWhiteSpace(bridgeError)
                    ? "Waiting for Windows Media Player playback"
                    : "Windows Media Player could not be read";
                Publish(new DiscordRpcStatus(
                    summary,
                    "",
                    $"Fallback asset: wmp_empty\n{_playbackBridge.ConnectionState}",
                    bridgeError,
                    null));
                return;
            }

            _handoffBuffer.Reset();
            var elapsedSinceUtc = _elapsedClock.Observe(playback, now);
            var payload = DiscordPresenceFormatter.Create(playback, _preferences.Mappings, elapsedSinceUtc);
            if (payload is null) return;

            if (!Equals(payload, _lastPayload))
            {
                _client?.SetPresence(new RichPresence
                {
                    Details = payload.Details,
                    State = payload.State,
                    Assets = new Assets
                    {
                        LargeImageKey = payload.LargeImageKey,
                        LargeImageText = payload.LargeImageText,
                        SmallImageKey = payload.SmallImageKey,
                        SmallImageText = payload.SmallImageText
                    },
                    Timestamps = payload.ElapsedSinceUtc is { } elapsedSince
                        ? new Timestamps(elapsedSince.UtcDateTime)
                        : null
                });
                _lastPayload = payload;
            }

            Publish(new DiscordRpcStatus(
                "Discord Rich Presence is active",
                $"{payload.Details} — {payload.State}",
                payload.UsesFallbackArtwork ? "Using fallback Discord asset: wmp_empty" : $"Using Discord asset: {payload.LargeImageKey}",
                "",
                now));
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Publish(new DiscordRpcStatus("Discord Rich Presence update failed", "", "", FriendlyError(error), null));
        }
        finally
        {
            _tickGate.Release();
        }
    }

    private void StopCore(bool publishDisabled)
    {
        var cancellation = Interlocked.Exchange(ref _cancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        _monitor = null;
        ClearPresence();
        _client?.Dispose();
        _client = null;
        _playbackBridge.Disconnect();
        _stateTracker.Reset();
        _elapsedClock.Reset();
        _handoffBuffer.Reset();
        _lastPayload = null;
        lock (_gate) _currentPlayback = null;
        if (publishDisabled)
            Publish(new DiscordRpcStatus("Discord Rich Presence is disabled", "", "", "", null));
    }

    private void ClearPresence()
    {
        if (_lastPayload is null) return;
        try { _client?.ClearPresence(); }
        catch { }
        _lastPayload = null;
    }

    private void Publish(DiscordRpcStatus status) => StatusChanged?.Invoke(status);
    private static string FriendlyError(Exception error) => string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
}
