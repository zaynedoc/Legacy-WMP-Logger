namespace WmplWrap;

public static class SnapshotStatus
{
    public static string Describe(IReadOnlyList<LibrarySnapshot> snapshots) => snapshots.Count == 0
        ? "No snapshots yet. Run: snapshot"
        : $"{snapshots.Count:N0} snapshot(s); first: {snapshots[0].CapturedAtUtc:u}; latest: {snapshots[^1].CapturedAtUtc:u}";
}
