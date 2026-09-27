using System.Text.Json;

namespace WmplWrap;

public sealed class SnapshotStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _snapshotDirectory;

    public SnapshotStore(string dataDirectory)
    {
        _snapshotDirectory = Path.Combine(Path.GetFullPath(dataDirectory), "snapshots");
    }

    public void Save(LibrarySnapshot snapshot)
    {
        Directory.CreateDirectory(_snapshotDirectory);
        var file = Path.Combine(_snapshotDirectory, $"{snapshot.CapturedAtUtc:yyyyMMddTHHmmssfffZ}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(snapshot, Json));
    }

    public IReadOnlyList<LibrarySnapshot> LoadAll() =>
        !Directory.Exists(_snapshotDirectory)
            ? []
            : Directory.EnumerateFiles(_snapshotDirectory, "*.json")
                .Select(path => JsonSerializer.Deserialize<LibrarySnapshot>(File.ReadAllText(path), Json)
                    ?? throw new InvalidDataException($"Could not read snapshot '{path}'."))
                .OrderBy(snapshot => snapshot.CapturedAtUtc)
                .ToArray();
}
