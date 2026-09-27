namespace WmplWrap;

public static class Reporting
{
    public static PeriodReport Compare(LibrarySnapshot start, LibrarySnapshot end)
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
}
