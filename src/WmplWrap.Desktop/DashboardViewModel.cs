using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WmplWrap.Desktop;

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private const string ScheduledTaskName = "WMPL Wrap Daily Snapshot";
    private const string DefaultRepositoryUrl = "https://github.com/zaynedoc/Legacy-WMP-Logger";
    private static string _sessionPeriod = "All time";
    private readonly SnapshotStore _store;
    private DashboardPage _page = DashboardPage.Overview;
    private DataView _dataView = DataView.Tracks;
    private bool _isCapturing;
    private string _snapshotSummary = "Loading local snapshot history";
    private string _latestSnapshotCaption = "";
    private string _latestSnapshotSummary = "";
    private string _latestSnapshotEmptyMessage = "";
    private string _topCaption = "";
    private string _topEmptyMessage = "";
    private string _totalListens = "-";
    private string _totalListeningTime = "-";
    private string _metricPeriodCaption = "All time";
    private string _metricTopArtist = "-";
    private string _metricTopArtistCount = "";
    private string _schedulerState = "Checking automatic snapshots";

    public DashboardViewModel()
    {
        DataDirectory = Environment.GetEnvironmentVariable("WMPL_WRAP_DATA") ?? Path.Combine(Environment.CurrentDirectory, "data");
        _store = new SnapshotStore(DataDirectory);
        RefreshCommand = new RelayCommand(_ => Refresh());
        CaptureCommand = new AsyncRelayCommand(CaptureAsync, () => !_isCapturing);
        NavigateCommand = new RelayCommand(parameter => Navigate(parameter?.ToString()));
        BackCommand = new RelayCommand(_ => MovePage(-1));
        ForwardCommand = new RelayCommand(_ => MovePage(1));
        OpenSchedulerCommand = new RelayCommand(_ => OpenScheduler());
        DisableSchedulerCommand = new AsyncRelayCommand(DisableSchedulerAsync, () => true);
        OpenGitHubCommand = new RelayCommand(_ => OpenGitHub());
        Refresh();
        RefreshSchedulerState();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<DashboardSnapshotChange> LatestSnapshotTracks { get; } = [];
    public ObservableCollection<DashboardSong> TopTracks { get; } = [];
    public ObservableCollection<DashboardAggregate> TopAlbums { get; } = [];
    public ObservableCollection<DashboardAggregate> TopArtists { get; } = [];
    public ObservableCollection<DashboardDataRow> DataRows { get; } = [];
    public IReadOnlyList<string> PeriodOptions { get; } = ["All time", "Past week", "Past month", "Past year"];
    public ICommand RefreshCommand { get; }
    public ICommand CaptureCommand { get; }
    public ICommand NavigateCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand ForwardCommand { get; }
    public ICommand OpenSchedulerCommand { get; }
    public ICommand DisableSchedulerCommand { get; }
    public ICommand OpenGitHubCommand { get; }
    public string DataDirectory { get; }
    public string DataLocationLabel => $"Data: {DataDirectory}";
    public string SnapshotSummary { get => _snapshotSummary; private set => Set(ref _snapshotSummary, value); }
    public string LatestSnapshotCaption { get => _latestSnapshotCaption; private set => Set(ref _latestSnapshotCaption, value); }
    public string LatestSnapshotSummary { get => _latestSnapshotSummary; private set => Set(ref _latestSnapshotSummary, value); }
    public string LatestSnapshotEmptyMessage { get => _latestSnapshotEmptyMessage; private set => Set(ref _latestSnapshotEmptyMessage, value); }
    public string TopCaption { get => _topCaption; private set => Set(ref _topCaption, value); }
    public string TopEmptyMessage { get => _topEmptyMessage; private set => Set(ref _topEmptyMessage, value); }
    public string TotalListens { get => _totalListens; private set => Set(ref _totalListens, value); }
    public string TotalListeningTime { get => _totalListeningTime; private set => Set(ref _totalListeningTime, value); }
    public string MetricPeriodCaption { get => _metricPeriodCaption; private set => Set(ref _metricPeriodCaption, value); }
    public string MetricTopArtist { get => _metricTopArtist; private set => Set(ref _metricTopArtist, value); }
    public string MetricTopArtistCount { get => _metricTopArtistCount; private set => Set(ref _metricTopArtistCount, value); }
    public string SchedulerState { get => _schedulerState; private set => Set(ref _schedulerState, value); }
    public string CaptureButtonText => _isCapturing ? "Reading WMP" : "Capture snapshot";
    public bool CanCapture => !_isCapturing;
    public string Breadcrumbs => _page switch
    {
        DashboardPage.LatestSnapshot => "Library  >  Latest snapshot",
        DashboardPage.Graphs => "Library  >  Graphs",
        DashboardPage.Data => $"Data  >  {DataTitle}",
        DashboardPage.Settings => "Library  >  Settings",
        _ => "Library  >  Listening history"
    };
    public Visibility OverviewVisibility => _page == DashboardPage.Overview ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LatestSnapshotVisibility => _page == DashboardPage.LatestSnapshot ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GraphsVisibility => _page == DashboardPage.Graphs ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DataVisibility => _page == DashboardPage.Data ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SettingsVisibility => _page == DashboardPage.Settings ? Visibility.Visible : Visibility.Collapsed;
    public bool CanGoBack => _page != DashboardPage.Overview;
    public bool CanGoForward => _page != DashboardPage.Settings;
    public string DataTitle => _dataView switch { DataView.Albums => "Top albums", DataView.Artists => "Top artists", _ => "Top songs" };
    public string DataCaption => TopCaption;
    public string DataContextColumn => _dataView switch { DataView.Albums => "TRACKS", DataView.Artists => "TOP ALBUM", _ => "ALBUM" };

    public string SelectedPeriod
    {
        get => _sessionPeriod;
        set
        {
            if (_sessionPeriod == value) return;
            _sessionPeriod = value;
            OnPropertyChanged();
            PopulateInsights(_store.LoadAll());
        }
    }

    private void Navigate(string? target)
    {
        if (target?.StartsWith("Data:", StringComparison.OrdinalIgnoreCase) == true)
        {
            _dataView = Enum.TryParse<DataView>(target[5..], true, out var view) ? view : DataView.Tracks;
            _page = DashboardPage.Data;
            Refresh();
        }
        else
        {
            _page = Enum.TryParse<DashboardPage>(target, true, out var page) ? page : DashboardPage.Overview;
        }
        OnPropertyChanged(nameof(Breadcrumbs));
        OnPropertyChanged(nameof(OverviewVisibility));
        OnPropertyChanged(nameof(LatestSnapshotVisibility));
        OnPropertyChanged(nameof(GraphsVisibility));
        OnPropertyChanged(nameof(DataVisibility));
        OnPropertyChanged(nameof(SettingsVisibility));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(DataTitle));
        OnPropertyChanged(nameof(DataCaption));
        OnPropertyChanged(nameof(DataContextColumn));
        if (_page == DashboardPage.Settings) RefreshSchedulerState();
    }

    private void MovePage(int direction)
    {
        var next = Math.Clamp((int)_page + direction, (int)DashboardPage.Overview, (int)DashboardPage.Settings);
        Navigate(((DashboardPage)next).ToString());
    }

    private void Refresh()
    {
        try { Populate(_store.LoadAll()); }
        catch
        {
            ClearAll();
            SnapshotSummary = "The local snapshot history could not be read";
            LatestSnapshotCaption = "Snapshot history unavailable";
            LatestSnapshotSummary = "No interval data is available";
            LatestSnapshotEmptyMessage = "Check the data location shown below";
            TopCaption = "Snapshot history unavailable";
            TopEmptyMessage = "Check the data location shown below";
        }
    }

    private async Task CaptureAsync()
    {
        _isCapturing = true;
        OnPropertyChanged(nameof(CaptureButtonText));
        OnPropertyChanged(nameof(CanCapture));
        try
        {
            var snapshot = await StaWorker.Run(() => new WmpLibraryScanner().Capture(DateTimeOffset.UtcNow));
            _store.Save(snapshot);
            Populate(_store.LoadAll());
        }
        finally
        {
            _isCapturing = false;
            OnPropertyChanged(nameof(CaptureButtonText));
            OnPropertyChanged(nameof(CanCapture));
        }
    }

    private void Populate(IReadOnlyList<LibrarySnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            ClearAll();
            SnapshotSummary = "No local baseline yet";
            LatestSnapshotCaption = "No changes observed";
            LatestSnapshotSummary = "Capture a first snapshot to begin";
            LatestSnapshotEmptyMessage = "Capture a first snapshot to begin";
            TopCaption = "Waiting for a WMP library snapshot";
            TopEmptyMessage = "Your first snapshot will populate this list";
            MetricPeriodCaption = SelectedPeriod;
            return;
        }

        var latest = snapshots[^1];
        SnapshotSummary = snapshots.Count == 1
            ? $"First baseline saved {ToEastern(latest.CapturedAtUtc):MMM d h:mm tt}"
            : $"{snapshots.Count:N0} snapshots recorded  ·  latest {ToEastern(latest.CapturedAtUtc):MMM d h:mm tt}";
        PopulateLatestSnapshot(snapshots);
        PopulateInsights(snapshots);
    }

    private void PopulateLatestSnapshot(IReadOnlyList<LibrarySnapshot> snapshots)
    {
        LatestSnapshotTracks.Clear();
        if (snapshots.Count < 2)
        {
            LatestSnapshotCaption = "Needs a second snapshot";
            LatestSnapshotSummary = "The first snapshot is your baseline";
            LatestSnapshotEmptyMessage = "Capture one more snapshot to calculate new listens";
            return;
        }

        var earlier = snapshots[^2];
        var latest = snapshots[^1];
        var report = Reporting.Compare(earlier, latest);
        var increases = report.Rows.Where(row => row.Listens > 0).ToArray();
        LatestSnapshotCaption = $"Changes from {ToEastern(earlier.CapturedAtUtc):MMM d h:mm tt} to {ToEastern(latest.CapturedAtUtc):MMM d h:mm tt}";
        LatestSnapshotSummary = increases.Length == 0
            ? "No observed play-count increases"
            : $"{increases.Length:N0} tracks with {increases.Sum(row => row.Listens):N0} new listens";
        foreach (var row in increases)
            LatestSnapshotTracks.Add(DashboardSnapshotChange.From(row.Track, row.Listens, LatestSnapshotTracks.Count + 1));
        LatestSnapshotEmptyMessage = "No play-count increases observed between the latest snapshots";
    }

    private void PopulateInsights(IReadOnlyList<LibrarySnapshot> snapshots)
    {
        TopTracks.Clear();
        TopAlbums.Clear();
        TopArtists.Clear();
        var entries = GetPeriodTracks(snapshots, out var caption, out var emptyMessage, out var metricPeriodCaption);
        MetricPeriodCaption = metricPeriodCaption;
        TopCaption = caption;
        TopEmptyMessage = emptyMessage;
        OnPropertyChanged(nameof(DataCaption));

        foreach (var entry in entries.OrderByDescending(entry => entry.Count).ThenBy(entry => entry.Track.Title).Take(5))
            TopTracks.Add(DashboardSong.From(entry.Track, $"{entry.Count:N0}", TopTracks.Count + 1));

        foreach (var group in entries.GroupBy(entry => EmptyAsUnknown(entry.Track.Album, "Unknown album"))
                     .Select(group => new { Name = group.Key, Count = group.Sum(entry => entry.Count), First = group.First().Track })
                     .OrderByDescending(group => group.Count).ThenBy(group => group.Name).Take(5))
            TopAlbums.Add(new DashboardAggregate(group.Name, group.First.Artist, $"{group.Count:N0}", AlbumArtResolver.For(group.First)));

        foreach (var group in entries.GroupBy(entry => EmptyAsUnknown(entry.Track.Artist, "Unknown artist"))
                     .Select(group => new { Name = group.Key, Count = group.Sum(entry => entry.Count), First = group.First().Track })
                     .OrderByDescending(group => group.Count).ThenBy(group => group.Name).Take(5))
            TopArtists.Add(new DashboardAggregate(group.Name, group.First.Album, $"{group.Count:N0}", AlbumArtResolver.For(group.First)));

        PopulateDataRows(entries);

        TotalListens = entries.Sum(entry => entry.Count).ToString("N0", CultureInfo.CurrentCulture);
        TotalListeningTime = FormatDuration(entries.Sum(entry => DurationSeconds(entry.Track.Duration) * entry.Count));
        var topArtist = TopArtists.FirstOrDefault();
        MetricTopArtist = topArtist?.Title ?? "-";
        MetricTopArtistCount = topArtist is null ? "" : $"{topArtist.CountLabel} listens";
    }

    private void PopulateDataRows(IReadOnlyList<TrackTally> entries)
    {
        DataRows.Clear();
        if (_dataView == DataView.Tracks)
        {
            foreach (var entry in entries.OrderByDescending(entry => entry.Count).ThenBy(entry => entry.Track.Title))
                DataRows.Add(new DashboardDataRow(DataRows.Count + 1, entry.Track.Title, EmptyAsUnknown(entry.Track.Artist, "Unknown artist"), EmptyAsUnknown(entry.Track.Album, "Unknown album"), $"{entry.Count:N0}", AlbumArtResolver.For(entry.Track)));
            return;
        }

        if (_dataView == DataView.Albums)
        {
            foreach (var group in entries.GroupBy(entry => EmptyAsUnknown(entry.Track.Album, "Unknown album"))
                         .Select(group => new { Name = group.Key, Count = group.Sum(entry => entry.Count), TrackCount = group.Count(), First = group.First().Track })
                         .OrderByDescending(group => group.Count).ThenBy(group => group.Name))
                DataRows.Add(new DashboardDataRow(DataRows.Count + 1, group.Name, EmptyAsUnknown(group.First.Artist, "Unknown artist"), $"{group.TrackCount:N0} tracks", $"{group.Count:N0}", AlbumArtResolver.For(group.First)));
            return;
        }

        foreach (var group in entries.GroupBy(entry => EmptyAsUnknown(entry.Track.Artist, "Unknown artist"))
                     .Select(group => new { Name = group.Key, Count = group.Sum(entry => entry.Count), First = group.First().Track })
                     .OrderByDescending(group => group.Count).ThenBy(group => group.Name))
            DataRows.Add(new DashboardDataRow(DataRows.Count + 1, group.Name, EmptyAsUnknown(group.First.Album, "Unknown album"), group.First.Album, $"{group.Count:N0}", AlbumArtResolver.For(group.First)));
    }

    private IReadOnlyList<TrackTally> GetPeriodTracks(IReadOnlyList<LibrarySnapshot> snapshots, out string caption, out string emptyMessage, out string metricPeriodCaption)
    {
        if (snapshots.Count == 0)
        {
            caption = "Waiting for a WMP library snapshot";
            emptyMessage = "Your first snapshot will populate this list";
            metricPeriodCaption = SelectedPeriod;
            return [];
        }

        var latest = snapshots[^1];
        if (SelectedPeriod == "All time")
        {
            caption = "Current cumulative WMP play counts";
            emptyMessage = "No audio tracks found in the latest snapshot";
            metricPeriodCaption = "All time";
            return latest.Tracks.Where(track => track.PlayCount > 0).Select(track => new TrackTally(track, track.PlayCount)).ToArray();
        }

        if (snapshots.Count < 2)
        {
            caption = "Needs a second snapshot";
            emptyMessage = "Capture one more snapshot to calculate new listens";
            metricPeriodCaption = SelectedPeriod;
            return [];
        }

        var span = SelectedPeriod switch { "Past week" => TimeSpan.FromDays(7), "Past month" => TimeSpan.FromDays(31), "Past year" => TimeSpan.FromDays(365), _ => TimeSpan.Zero };
        var start = snapshots.LastOrDefault(snapshot => snapshot.CapturedAtUtc <= latest.CapturedAtUtc - span);
        if (start is null)
        {
            start = snapshots[0];
            var baseline = ToEastern(start.CapturedAtUtc);
            caption = $"Observed since {baseline:MMM d h:mm tt}";
            emptyMessage = "No play-count increases observed since the first snapshot";
            metricPeriodCaption = $"Since {baseline:MMM d}";
            return Reporting.Compare(start, latest).Rows.Where(row => row.Listens > 0).Select(row => new TrackTally(row.Track, row.Listens)).ToArray();
        }

        caption = $"Observed from {ToEastern(start.CapturedAtUtc):MMM d} to {ToEastern(latest.CapturedAtUtc):MMM d}";
        emptyMessage = "No play-count increases observed in this period";
        metricPeriodCaption = SelectedPeriod;
        return Reporting.Compare(start, latest).Rows.Where(row => row.Listens > 0).Select(row => new TrackTally(row.Track, row.Listens)).ToArray();
    }

    private void RefreshSchedulerState()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{ScheduledTaskName}\"") { CreateNoWindow = true, UseShellExecute = false });
            process?.WaitForExit(3000);
            SchedulerState = process?.ExitCode == 0 ? "Daily snapshots are enabled" : "No automatic snapshot task found";
        }
        catch { SchedulerState = "Unable to check automatic snapshots"; }
    }

    private async Task DisableSchedulerAsync()
    {
        var disabled = await Task.Run(() =>
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("schtasks.exe", $"/Delete /TN \"{ScheduledTaskName}\" /F") { CreateNoWindow = true, UseShellExecute = false });
                process?.WaitForExit();
                return process?.ExitCode == 0;
            }
            catch { return false; }
        });
        SchedulerState = disabled ? "Automatic snapshots disabled" : "No automatic snapshot task found";
    }

    private static void OpenScheduler() => Process.Start(new ProcessStartInfo("taskschd.msc") { UseShellExecute = true });
    private static void OpenGitHub() => Process.Start(new ProcessStartInfo(Environment.GetEnvironmentVariable("WMPL_WRAP_REPOSITORY_URL") ?? DefaultRepositoryUrl) { UseShellExecute = true });
    private static string EmptyAsUnknown(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
    private static double DurationSeconds(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
    private static string FormatDuration(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} m" : $"{span.Minutes} m";
    }
    private void ClearAll() { LatestSnapshotTracks.Clear(); TopTracks.Clear(); TopAlbums.Clear(); TopArtists.Clear(); DataRows.Clear(); TotalListens = "-"; TotalListeningTime = "-"; MetricTopArtist = "-"; MetricTopArtistCount = ""; }
    private static DateTimeOffset ToEastern(DateTimeOffset utc) => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(utc, "Eastern Standard Time");
    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; OnPropertyChanged(property); }
    private void OnPropertyChanged([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

public sealed record DashboardSong(int Rank, string Title, string ArtistAlbum, string CountLabel, ImageSource? Artwork)
{
    public static DashboardSong From(TrackSnapshot track, string count, int rank) => new(rank, track.Title, string.IsNullOrWhiteSpace(track.Album) ? track.Artist : $"{track.Artist} · {track.Album}", count, AlbumArtResolver.For(track));
}

public sealed record DashboardAggregate(string Title, string Subtitle, string CountLabel, ImageSource? Artwork);
public sealed record DashboardDataRow(int Rank, string Title, string Subtitle, string Context, string CountLabel, ImageSource? Artwork);
public sealed record DashboardSnapshotChange(int Rank, string Title, string Artist, string Album, string Duration, string ListensLabel, string TotalCountLabel, ImageSource? Artwork)
{
    public static DashboardSnapshotChange From(TrackSnapshot track, long listens, int rank) => new(
        rank,
        track.Title,
        string.IsNullOrWhiteSpace(track.Artist) ? "Unknown artist" : track.Artist,
        string.IsNullOrWhiteSpace(track.Album) ? "Unknown album" : track.Album,
        FormatTrackDuration(track.Duration),
        $"+{listens:N0}",
        track.PlayCount.ToString("N0", CultureInfo.CurrentCulture),
        AlbumArtResolver.For(track));

    private static string FormatTrackDuration(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) return "—";
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}" : $"{span.Minutes}:{span.Seconds:D2}";
    }
}
internal sealed record TrackTally(TrackSnapshot Track, long Count);
internal enum DashboardPage { Overview, LatestSnapshot, Graphs, Data, Settings }
internal enum DataView { Tracks, Albums, Artists }

internal sealed class RelayCommand(Action<object?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute(parameter);
}

internal sealed class AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? parameter) => canExecute();
    public async void Execute(object? parameter) { if (canExecute()) await execute(); }
}

internal static class StaWorker
{
    public static Task<T> Run<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { completion.SetResult(work()); } catch (Exception ex) { completion.SetException(ex); } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
