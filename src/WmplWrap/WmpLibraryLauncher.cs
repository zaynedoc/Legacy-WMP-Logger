using System.Globalization;
using System.Runtime.InteropServices;

namespace WmplWrap;

public enum WmpOpenKind { Track, Album, Artist }

public sealed record WmpOpenTarget(WmpOpenKind Kind, string SourceUrl, string Label);

public static class WmpLibraryLauncher
{
    public static void Open(WmpOpenTarget target)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows Media Player is available only on Windows.");

        var type = Type.GetTypeFromProgID("WMPlayer.OCX")
            ?? throw new InvalidOperationException("Windows Media Player is not registered on this computer.");
        dynamic? player = null;
        dynamic? playlist = null;
        dynamic? media = null;

        try
        {
            player = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("Windows Media Player could not be started.");

            var sourceUrl = target.SourceUrl;
            if (target.Kind == WmpOpenKind.Album && !string.IsNullOrWhiteSpace(target.Label))
            {
                try
                {
                    playlist = player.mediaCollection.getByAlbum(target.Label);
                    sourceUrl = FirstSourceUrl(playlist, sourceUrl, ref media);
                }
                catch { /* Fall back to the matching source saved in the snapshot. */ }
            }
            else if (target.Kind == WmpOpenKind.Artist && !string.IsNullOrWhiteSpace(target.Label))
            {
                try
                {
                    playlist = player.mediaCollection.getByAuthor(target.Label);
                    sourceUrl = FirstSourceUrl(playlist, sourceUrl, ref media);
                }
                catch { /* Fall back to the matching source saved in the snapshot. */ }
            }

            if (string.IsNullOrWhiteSpace(sourceUrl))
                throw new InvalidOperationException("No playable local media file is available for this item.");

            player.openPlayer(sourceUrl);
        }
        finally
        {
            Release(media);
            Release(playlist);
            Release(player);
        }
    }

    private static string FirstSourceUrl(dynamic playlist, string fallback, ref dynamic? media)
    {
        var count = Convert.ToInt32(playlist.count, CultureInfo.InvariantCulture);
        if (count == 0) return fallback;

        media = playlist.Item(0);
        try
        {
            var sourceUrl = Convert.ToString(media.getItemInfo("SourceURL"), CultureInfo.InvariantCulture)?.Trim();
            return string.IsNullOrWhiteSpace(sourceUrl) ? fallback : sourceUrl!;
        }
        catch { return fallback; }
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject)) Marshal.FinalReleaseComObject(comObject);
    }
}
