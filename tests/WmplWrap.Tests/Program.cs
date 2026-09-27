using WmplWrap;

var first = new LibrarySnapshot(1, new DateTimeOffset(2026, 9, 20, 4, 5, 0, TimeSpan.Zero), "Eastern Standard Time",
[
    Track("one", "Existing", 10),
    Track("two", "Reset", 9)
]);
var second = new LibrarySnapshot(1, new DateTimeOffset(2026, 9, 23, 4, 5, 0, TimeSpan.Zero), "Eastern Standard Time",
[
    Track("one", "Existing", 14),
    Track("two", "Reset", 2),
    Track("three", "New without a baseline", 7)
]);

var report = Reporting.Compare(first, second);
Assert(report.Rows.Count == 2, "Only the changed established track and reset marker should appear.");
Assert(report.Rows.Single(row => row.Track.Id == "one").Listens == 4, "Cumulative-count delta should be 4.");
Assert(report.Rows.Single(row => row.Track.Id == "two").CounterWentBackwards, "A decrease should be flagged as a reset.");
Assert(report.Rows.Single(row => row.Track.Id == "two").Listens == 0, "A reset must never become negative or invented listens.");
Assert(report.NewTracksWithoutBaseline == 1, "New tracks must be excluded until their next baseline.");
Console.WriteLine("All reporting tests passed.");

static TrackSnapshot Track(string id, string title, long count) => new(id, $"file:///{id}.mp3", title, "Artist", "Album", "03:00", count);
static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
