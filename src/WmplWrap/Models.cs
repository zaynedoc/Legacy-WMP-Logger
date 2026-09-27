namespace WmplWrap;

public sealed record TrackSnapshot(
    string Id,
    string SourceUrl,
    string Title,
    string Artist,
    string Album,
    string Duration,
    long PlayCount);

public sealed record LibrarySnapshot(
    int SchemaVersion,
    DateTimeOffset CapturedAtUtc,
    string TimeZoneId,
    IReadOnlyList<TrackSnapshot> Tracks);

public sealed record ReportRow(
    TrackSnapshot Track,
    long Listens,
    bool CounterWentBackwards);

public sealed record PeriodReport(
    LibrarySnapshot StartSnapshot,
    LibrarySnapshot EndSnapshot,
    IReadOnlyList<ReportRow> Rows,
    int NewTracksWithoutBaseline,
    int CounterResets);
