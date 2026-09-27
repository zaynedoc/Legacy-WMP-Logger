using System.Globalization;

namespace WmplWrap;

internal static class Program
{
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
            var options = ParseOptions(args.Skip(1).ToArray());
            var store = new SnapshotStore(options.TryGetValue("data", out var directory) ? directory : Path.Combine(Environment.CurrentDirectory, "data"));

            return command switch
            {
                "snapshot" => Snapshot(store),
                "status" => Status(store),
                "report" => Report(store, options),
                "demo-report" => DemoReport(),
                "help" or "--help" or "-h" => Help(),
                _ => Fail($"Unknown command '{command}'.")
            };
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Error: {error.Message}");
            return 1;
        }
    }

    private static int Snapshot(SnapshotStore store)
    {
        var snapshot = new WmpLibraryScanner().Capture(DateTimeOffset.UtcNow);
        store.Save(snapshot);
        Console.WriteLine($"Baseline/snapshot saved at {snapshot.CapturedAtUtc:u} with {snapshot.Tracks.Count:N0} audio tracks.");
        return 0;
    }

    private static int Status(SnapshotStore store)
    {
        var snapshots = store.LoadAll();
        Console.WriteLine(snapshots.Count == 0
            ? "No snapshots yet. Run: snapshot"
            : $"{snapshots.Count:N0} snapshot(s); first: {snapshots[0].CapturedAtUtc:u}; latest: {snapshots[^1].CapturedAtUtc:u}");
        return 0;
    }

    private static int Report(SnapshotStore store, IReadOnlyDictionary<string, string> options)
    {
        if (!options.TryGetValue("from", out var fromText) || !options.TryGetValue("to", out var toText))
            return Fail("report requires --from and --to.");
        var from = ParseBoundary(fromText, false);
        var to = ParseBoundary(toText, true);
        if (to < from) return Fail("--to must not be earlier than --from.");

        var snapshots = store.LoadAll();
        var start = snapshots.LastOrDefault(snapshot => snapshot.CapturedAtUtc <= from)
            ?? throw new InvalidOperationException("No snapshot exists at or before --from; the first snapshot is your baseline.");
        var end = snapshots.LastOrDefault(snapshot => snapshot.CapturedAtUtc <= to)
            ?? throw new InvalidOperationException("No snapshot exists at or before --to.");
        if (end.CapturedAtUtc <= start.CapturedAtUtc)
            return Fail("The selected range needs a later end snapshot. Capture another snapshot first.");

        var report = Reporting.Compare(start, end);
        var top = options.TryGetValue("top", out var topText) && int.TryParse(topText, out var requested) ? Math.Max(1, requested) : 10;
        RenderReport(report, top);
        return 0;
    }

    private static int DemoReport()
    {
        var start = new LibrarySnapshot(1, new DateTimeOffset(2026, 9, 21, 4, 5, 0, TimeSpan.Zero), "Eastern Standard Time",
        [
            DemoTrack("yorushika-dive", "DIVE", "Yorushika", "Elma", 18),
            DemoTrack("eve-dramaturgy", "Dramaturgy", "Eve", "OFFICIAL NUMBER", 31),
            DemoTrack("zutomayo-haze", "Haze Haseru Haterumade", "Zutomayo", "Hisohiso Banashi", 12),
            DemoTrack("reset-example", "Example of a WMP reset", "Demo Artist", "Demo Album", 9)
        ]);
        var end = new LibrarySnapshot(1, new DateTimeOffset(2026, 9, 27, 4, 5, 0, TimeSpan.Zero), "Eastern Standard Time",
        [
            DemoTrack("yorushika-dive", "DIVE", "Yorushika", "Elma", 29),
            DemoTrack("eve-dramaturgy", "Dramaturgy", "Eve", "OFFICIAL NUMBER", 38),
            DemoTrack("zutomayo-haze", "Haze Haseru Haterumade", "Zutomayo", "Hisohiso Banashi", 15),
            DemoTrack("reset-example", "Example of a WMP reset", "Demo Artist", "Demo Album", 2),
            DemoTrack("new-track", "Newly added track", "Demo Artist", "Demo Album", 5)
        ]);

        Console.WriteLine("DEMO DATA — no WMP library or saved snapshot data was read or changed.");
        RenderReport(Reporting.Compare(start, end), 10);
        return 0;
    }

    private static TrackSnapshot DemoTrack(string id, string title, string artist, string album, long plays) =>
        new(id, $"demo://{id}", title, artist, album, "03:30", plays);

    private static void RenderReport(PeriodReport report, int top)
    {
        Console.WriteLine($"Observed interval: {report.StartSnapshot.CapturedAtUtc:u} to {report.EndSnapshot.CapturedAtUtc:u}");
        Console.WriteLine($"Total observed listens: {report.Rows.Sum(row => row.Listens):N0}");
        foreach (var row in report.Rows.Take(top))
        {
            var warning = row.CounterWentBackwards ? "  [WMP count decreased: counted as 0]" : "";
            Console.WriteLine($"{row.Listens,5:N0}  {Label(row.Track)}{warning}");
        }
        if (report.NewTracksWithoutBaseline > 0)
            Console.WriteLine($"Note: {report.NewTracksWithoutBaseline:N0} new track(s) omitted because they have no start baseline.");
        if (report.CounterResets > 0)
            Console.WriteLine($"Note: {report.CounterResets:N0} counter reset(s) were not counted as listens.");
    }

    private static string Label(TrackSnapshot track) => string.IsNullOrWhiteSpace(track.Artist) ? track.Title : $"{track.Title} — {track.Artist}";

    private static DateTimeOffset ParseBoundary(string value, bool endOfDate)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            var local = date.ToDateTime(endOfDate ? TimeOnly.MaxValue : TimeOnly.MinValue, DateTimeKind.Unspecified);
            return new DateTimeOffset(local, Eastern.GetUtcOffset(local)).ToUniversalTime();
        }
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant))
            return instant.ToUniversalTime();
        throw new ArgumentException($"'{value}' is not a yyyy-MM-dd date or ISO-8601 timestamp.");
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Expected an option, got '{args[i]}'.");
            if (++i >= args.Length) throw new ArgumentException($"Option '{args[i - 1]}' needs a value.");
            result[args[i - 1][2..]] = args[i];
        }
        return result;
    }

    private static int Help()
    {
        Console.WriteLine("WMPL Wrap\n  snapshot [--data <folder>]\n  status [--data <folder>]\n  report --from <date|instant> --to <date|instant> [--top <n>] [--data <folder>]\n  demo-report");
        return 0;
    }

    private static int Fail(string message) { Console.Error.WriteLine($"Error: {message}"); return 2; }
}
