using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace WmplWrap;

public sealed class WmpLibraryScanner
{
    public LibrarySnapshot Capture(DateTimeOffset now)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Media Player library access is available only on Windows.");

        var type = Type.GetTypeFromProgID("WMPlayer.OCX")
            ?? throw new InvalidOperationException("Windows Media Player is not registered on this computer.");
        dynamic? player = null;
        dynamic? playlist = null;
        var tracks = new Dictionary<string, TrackSnapshot>(StringComparer.OrdinalIgnoreCase);

        try
        {
            player = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("Windows Media Player could not be started.");
            playlist = player.mediaCollection.getAll();
            var count = Convert.ToInt32(playlist.count, CultureInfo.InvariantCulture);

            for (var index = 0; index < count; index++)
            {
                dynamic? media = null;
                try
                {
                    // The raw COM automation object exposes this as Item(index).
                    // get_Item is the name used by the generated WMPLib interop assembly.
                    media = playlist.Item(index);
                    var mediaType = Get(media, "MediaType");
                    if (!string.Equals(mediaType, "audio", StringComparison.OrdinalIgnoreCase)) continue;

                    var source = Get(media, "SourceURL");
                    var title = Get(media, "Title");
                    var artist = Get(media, "Author");
                    var album = Get(media, "WM/AlbumTitle");
                    var duration = Get(media, "Duration");
                    var track = new TrackSnapshot(
                        CreateId(source, title, artist, album, duration),
                        source, title, artist, album, duration,
                        ParseCount(Get(media, "UserPlayCount")), Get(media, "WM/AlbumCoverURL"));

                    // A source can appear more than once in WMP. Keep the highest observed count.
                    if (!tracks.TryGetValue(track.Id, out var existing) || track.PlayCount > existing.PlayCount)
                        tracks[track.Id] = track;
                }
                finally { Release(media); }
            }
        }
        finally
        {
            try { player?.close(); } catch { /* best-effort COM cleanup */ }
            Release(playlist);
            Release(player);
        }

        return new LibrarySnapshot(1, now.ToUniversalTime(), "Eastern Standard Time", tracks.Values
            .OrderBy(track => track.Id, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string Get(dynamic media, string attribute)
    {
        try { return Convert.ToString(media.getItemInfo(attribute), CultureInfo.InvariantCulture)?.Trim() ?? ""; }
        catch { return ""; }
    }

    private static long ParseCount(string value) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
        ? Math.Max(0, count) : 0;

    private static string CreateId(string source, string title, string artist, string album, string duration)
    {
        var identity = string.IsNullOrWhiteSpace(source)
            ? $"fallback\u001f{title}\u001f{artist}\u001f{album}\u001f{duration}"
            : $"source\u001f{source}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToUpperInvariant())));
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject)) Marshal.FinalReleaseComObject(comObject);
    }
}
