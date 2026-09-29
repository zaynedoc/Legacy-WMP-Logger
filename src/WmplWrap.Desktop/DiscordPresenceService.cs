using DiscordRPC;

namespace WmplWrap.Desktop;

internal enum DiscordRpcConnectionState { Inactive, Connecting, Active }

internal sealed record DiscordRpcStatus(
    string Summary,
    string NowPlaying,
    string Artwork,
    string LastError,
    DateTimeOffset? LastUpdatedAtUtc,
    DiscordRpcConnectionState ConnectionState);

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
    private DiscordRpcConnectionState _connectionState = DiscordRpcConnectionState.Inactive;
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
        SetConnectionState(DiscordRpcConnectionState.Connecting);
        PublishStatus("Connecting to Discord...", "", "", "", null);

        try
        {
            var client = new DiscordRpcClient(preferences.ApplicationId, -1, null, true, null);
            client.OnConnectionEstablished += (_, _) => UpdateConnectionState(client, DiscordRpcConnectionState.Connecting, "Discord IPC connected; waiting for ready confirmation", "");
            client.OnReady += (_, _) => UpdateConnectionState(client, DiscordRpcConnectionState.Active, "Discord connected; waiting for Windows Media Player playback", "");
            client.OnConnectionFailed += (_, _) => UpdateConnectionState(client, DiscordRpcConnectionState.Inactive, "Discord desktop is unavailable; start Discord and reconnect", "");
            client.OnClose += (_, _) => UpdateConnectionState(client, DiscordRpcConnectionState.Inactive, "Discord connection was closed; reconnect after Discord is available", "");
            _client = client;
            var initialized = client.Initialize();

            if (initialized)
                _monitor = Task.Run(() => MonitorAsync(_cancellation.Token));
            else
            {
                _client = null;
                client.Dispose();
                SetConnectionState(DiscordRpcConnectionState.Inactive);
                PublishStatus("Discord desktop is unavailable; start Discord and reconnect", "", "", "", null);
            }
        }
        catch (Exception error)
        {
            _client?.Dispose();
            _client = null;
            SetConnectionState(DiscordRpcConnectionState.Inactive);
            PublishStatus("Discord Rich Presence could not start", "", "", FriendlyError(error), null);
        }
    }

    public async Task RefreshAsync()
    {
        if (_client is null)
        {
            PublishStatus("Discord Rich Presence is not connected", "", "", "Use Save & reconnect after Discord is running.", null);
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
            var connectionState = ConnectionState;
            if (connectionState != DiscordRpcConnectionState.Active)
            {
                PublishStatus(
                    connectionState == DiscordRpcConnectionState.Connecting ? "Connecting to Discord..." : "Discord desktop is unavailable; start Discord and reconnect",
                    "",
                    "",
                    "",
                    null);
                return;
            }

            var playback = await _playbackBridge.ReadAsync(cancellationToken);
            playback = _stateTracker.Observe(playback, _preferences.DetectStalledPlayback);
            lock (_gate) _currentPlayback = playback.HasMedia ? playback : null;

            var now = DateTimeOffset.UtcNow;
            if (!playback.HasMedia)
            {
                if (_lastPayload is { } lastPayload &&
                    _handoffBuffer.ShouldKeepLastPresence(playback, _preferences.KeepPresenceBetweenTracks, now))
                {
                    PublishStatus(
                        "Holding the last Discord status while Windows Media Player changes tracks",
                        $"{lastPayload.Details} \u2014 {lastPayload.State}",
                        "Will clear after 5 seconds without playback",
                        "",
                        null);
                    return;
                }

                _elapsedClock.Reset();
                ClearPresence();
                var bridgeError = _playbackBridge.LastError;
                var summary = string.IsNullOrWhiteSpace(bridgeError)
                    ? "Waiting for Windows Media Player playback"
                    : "Windows Media Player could not be read";
                PublishStatus(
                    summary,
                    "",
                    $"Fallback asset: wmp_empty\n{_playbackBridge.ConnectionState}",
                    bridgeError,
                    null);
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
                    Buttons =
                    [
                        new DiscordRPC.Button
                        {
                            Label = "Google This Song",
                            Url = payload.GoogleSearchUrl
                        }
                    ],
                    Timestamps = payload.ElapsedSinceUtc is { } elapsedSince
                        ? new Timestamps(elapsedSince.UtcDateTime)
                        : null
                });
                _lastPayload = payload;
            }

            PublishStatus(
                "Discord Rich Presence is active",
                $"{payload.Details} — {payload.State}",
                payload.UsesFallbackArtwork ? "Using fallback Discord asset: wmp_empty" : $"Using Discord asset: {payload.LargeImageKey}",
                "",
                now);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            SetConnectionState(DiscordRpcConnectionState.Inactive);
            PublishStatus("Discord Rich Presence update failed", "", "", FriendlyError(error), null);
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
        var client = _client;
        _client = null;
        client?.Dispose();
        _playbackBridge.Disconnect();
        _stateTracker.Reset();
        _elapsedClock.Reset();
        _handoffBuffer.Reset();
        _lastPayload = null;
        lock (_gate) _currentPlayback = null;
        SetConnectionState(DiscordRpcConnectionState.Inactive);
        if (publishDisabled)
            PublishStatus("Discord Rich Presence is disabled", "", "", "", null);
    }

    private void ClearPresence()
    {
        if (_lastPayload is null) return;
        try { _client?.ClearPresence(); }
        catch { }
        _lastPayload = null;
    }

    private DiscordRpcConnectionState ConnectionState { get { lock (_gate) return _connectionState; } }

    private void UpdateConnectionState(
        DiscordRpcClient client,
        DiscordRpcConnectionState state,
        string summary,
        string error)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_client, client)) return;
            _connectionState = state;
        }
        PublishStatus(summary, "", "", error, null);
    }

    private void SetConnectionState(DiscordRpcConnectionState state)
    {
        lock (_gate) _connectionState = state;
    }

    private void PublishStatus(string summary, string nowPlaying, string artwork, string lastError, DateTimeOffset? lastUpdatedAtUtc) =>
        StatusChanged?.Invoke(new DiscordRpcStatus(summary, nowPlaying, artwork, lastError, lastUpdatedAtUtc, ConnectionState));

    private static string FriendlyError(Exception error) => string.IsNullOrWhiteSpace(error.Message) ? error.GetType().Name : error.Message;
}
