namespace C0n50le.Core.Widgets;

/// <summary>The pictures of the gallery widget: the images in a folder (and its subfolders), in a shuffled loop.</summary>
/// <summary>A picture's place in the widget's 2×2 grid.</summary>
public readonly record struct GalleryCell(int Row, int Column, int RowSpan, int ColumnSpan);

public sealed class Gallery
{
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };

    public const int MaxPictures = 500;

    private readonly IReadOnlyList<string> _pictures;
    private int _next;

    public Gallery(IReadOnlyList<string> pictures) => _pictures = pictures;

    public int Count => _pictures.Count;

    /// <summary>The picture at <paramref name="index"/>, wrapping around both ways (for the viewer's previous and next).</summary>
    public string? At(int index) => _pictures.Count == 0 ? null : _pictures[((index % _pictures.Count) + _pictures.Count) % _pictures.Count];

    public int IndexOf(string path) => _pictures is List<string> list ? list.IndexOf(path) : _pictures.ToList().IndexOf(path);

    /// <summary>The next picture, or null when the folder has none.</summary>
    public string? Next()
    {
        if (_pictures.Count == 0) return null;
        var picture = _pictures[_next % _pictures.Count];
        _next = (_next + 1) % _pictures.Count;
        return picture;
    }

    /// <summary>The widget shows up to this many pictures at once.</summary>
    public const int MaxShown = 4;

    /// <summary>The next pictures to show together: as many as the folder has, up to <paramref name="max"/>, never twice.</summary>
    public IReadOnlyList<string> NextBatch(int max = MaxShown)
    {
        var count = Math.Min(Math.Max(0, max), _pictures.Count);
        var batch = new List<string>(count);
        for (var index = 0; index < count; index++) batch.Add(Next()!);
        return batch;
    }

    /// <summary>
    /// Where each of <paramref name="count"/> pictures goes in the widget's grid: one fills it, two side by side, three as
    /// a large one on the left and two stacked on the right, four in a 2×2 grid.
    /// </summary>
    public static IReadOnlyList<GalleryCell> Layout(int count) => Math.Clamp(count, 0, MaxShown) switch
    {
        0 => [],
        1 => [new(0, 0, 2, 2)],
        2 => [new(0, 0, 2, 1), new(0, 1, 2, 1)],
        3 => [new(0, 0, 2, 1), new(0, 1, 1, 1), new(1, 1, 1, 1)],
        _ => [new(0, 0, 1, 1), new(0, 1, 1, 1), new(1, 0, 1, 1), new(1, 1, 1, 1)]
    };

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
