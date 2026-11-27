namespace C0n50le.Core.Widgets;

/// <summary>The pictures of the gallery widget: the images in a folder (and its subfolders), in a shuffled loop.</summary>
public sealed class Gallery
{
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

    public const int MaxPictures = 500;

    private readonly IReadOnlyList<string> _pictures;
    private int _next;

    public Gallery(IReadOnlyList<string> pictures) => _pictures = pictures;

    public int Count => _pictures.Count;

    /// <summary>The next picture, or null when the folder has none.</summary>
    public string? Next()
    {
        if (_pictures.Count == 0) return null;
        var picture = _pictures[_next % _pictures.Count];
        _next = (_next + 1) % _pictures.Count;
        return picture;
    }

    /// <summary>Reads the folder without failing on folders it may not open; a missing folder gives an empty gallery.</summary>
    public static Gallery Load(string folder, Random? random = null)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return new Gallery([]);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        var pictures = Directory.EnumerateFiles(folder, "*", options)
            .Where(file => Extensions.Contains(Path.GetExtension(file)))
            .Take(MaxPictures)
            .ToList();
        var shuffle = random ?? Random.Shared;
        for (var index = pictures.Count - 1; index > 0; index--)
        {
            var other = shuffle.Next(index + 1);
            (pictures[index], pictures[other]) = (pictures[other], pictures[index]);
        }
        return new Gallery(pictures);
    }
}
