using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WmplWrap.Desktop;

internal static class AlbumArtResolver
{
    private static readonly string[] PreferredNames = ["Folder.jpg", "folder.jpg", "Cover.jpg", "cover.jpg", "AlbumArt.jpg", "albumart.jpg", "Folder.png", "folder.png", "Cover.png", "cover.png"];
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? For(TrackSnapshot track)
    {
        var path = FindPath(track);
        if (path is not null) return FromFile(path);

        var source = LocalPath(track.SourceUrl);
        return source is null || !File.Exists(source) ? null : FromEmbeddedArt(source);
    }

    private static ImageSource? FromFile(string path)
    {
        if (Cache.TryGetValue(path, out var image)) return image;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 120;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return Cache[path] = bitmap;
        }
        catch
        {
            return Cache[path] = null;
        }
    }

    private static ImageSource? FromEmbeddedArt(string source)
    {
        var cacheKey = $"embedded:{source}";
        if (Cache.TryGetValue(cacheKey, out var image)) return image;
        try
        {
            using var media = TagLib.File.Create(source);
            var picture = media.Tag.Pictures
                .OrderByDescending(candidate => candidate.Type == TagLib.PictureType.FrontCover)
                .FirstOrDefault();
            if (picture is null) return Cache[cacheKey] = null;

            using var data = new MemoryStream(picture.Data.Data);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 120;
            bitmap.StreamSource = data;
            bitmap.EndInit();
            bitmap.Freeze();
            return Cache[cacheKey] = bitmap;
        }
        catch
        {
            return Cache[cacheKey] = null;
        }
    }

    private static string? FindPath(TrackSnapshot track)
    {
        var coverUrl = LocalPath(track.AlbumArtUrl);
        if (coverUrl is not null && File.Exists(coverUrl)) return coverUrl;

        var source = LocalPath(track.SourceUrl);
        if (source is null || !File.Exists(source)) return null;
        var directory = Path.GetDirectoryName(source);
        if (directory is null) return null;

        foreach (var name in PreferredNames)
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return candidate;
        }

        try
        {
            return Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                    || Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                    || Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string? LocalPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)) return uri.IsFile ? uri.LocalPath : null;
        return Path.IsPathFullyQualified(value) ? value : null;
    }
}
