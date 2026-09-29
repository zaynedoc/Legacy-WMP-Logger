namespace WmplWrap;

public static class Reporting
{
    public static PeriodReport Compare(LibrarySnapshot start, LibrarySnapshot end, bool includeFirstSeenCounts = false)
    {
        var startById = start.Tracks.ToDictionary(track => track.Id, StringComparer.OrdinalIgnoreCase);
        var rows = new List<ReportRow>();
        var newTracks = 0;
        var resets = 0;

        foreach (var current in end.Tracks)
        {
            if (!startById.TryGetValue(current.Id, out var baseline))
            {
                newTracks++;
                if (includeFirstSeenCounts && current.PlayCount > 0)
                    rows.Add(new ReportRow(current, current.PlayCount, false, current.PlayCount));
                continue;
            }

            var reset = current.PlayCount < baseline.PlayCount;
            if (reset) resets++;
            var listens = reset ? 0 : current.PlayCount - baseline.PlayCount;
            if (listens > 0 || reset) rows.Add(new ReportRow(current, listens, reset));
        }

        return new PeriodReport(start, end,
            rows.OrderByDescending(row => row.Listens).ThenBy(row => row.Track.Title, StringComparer.OrdinalIgnoreCase).ToArray(),
            newTracks, resets);
    }

    /// <summary>
    /// Aggregates report rows from every consecutive snapshot interval ending after the requested
    /// time. When enabled, a track's first recorded WMP count is attributed to the snapshot that
    /// first contained the track; later changes are always calculated as count deltas.
    /// </summary>
    public static IReadOnlyList<ReportRow> CompareIntervals(
        IReadOnlyList<LibrarySnapshot> snapshots,
        DateTimeOffset afterUtc,
        bool includeFirstSeenCounts)
    {
        if (snapshots.Count < 2) return [];

        var latestById = snapshots[^1].Tracks.ToDictionary(track => track.Id, StringComparer.OrdinalIgnoreCase);
        var totals = new Dictionary<string, (long Listens, long FirstSeenListens)>(StringComparer.OrdinalIgnoreCase);

        for (var index = 1; index < snapshots.Count; index++)
        {
            var end = snapshots[index];
            if (end.CapturedAtUtc <= afterUtc) continue;

            foreach (var row in Compare(snapshots[index - 1], end, includeFirstSeenCounts).Rows.Where(row => row.Listens > 0))
            {
                var prior = totals.GetValueOrDefault(row.Track.Id);
                totals[row.Track.Id] = (prior.Listens + row.Listens, prior.FirstSeenListens + row.FirstSeenListens);
            }
        }

        return totals
            .Where(entry => latestById.ContainsKey(entry.Key))
            .Select(entry => new ReportRow(latestById[entry.Key], entry.Value.Listens, false, entry.Value.FirstSeenListens))
            .OrderByDescending(row => row.Listens)
            .ThenBy(row => row.Track.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
