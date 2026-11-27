using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace C0n50le.Ui;

/// <summary>
/// The focus of a list row (settings, sidebar, menu options): the background turns into the platter and only the text and
/// icons grow. Each part grows from its own outer edge (the name from the left, the value from the right), so nothing
/// ever pushes past the row.
/// </summary>
public static class FocusZoom
{
    public const float Scale = 1.05f;

    public static void Attach(Control row)
    {
        row.GotFocus += (_, _) => Apply(row, Scale);
        row.LostFocus += (_, _) => Apply(row, 1f);
    }

    private static void Apply(Control row, float scale)
    {
        if (row is not ContentControl { Content: FrameworkElement content }) return;
        IEnumerable<UIElement> parts = content is Panel panel ? panel.Children : [content];
        foreach (var part in parts.OfType<FrameworkElement>())
        {
            if (part.ActualWidth <= 0) continue;
            var x = part.HorizontalAlignment switch
            {
                HorizontalAlignment.Right => part.ActualWidth,
                HorizontalAlignment.Center => part.ActualWidth / 2,
                _ => 0
            };
            part.ScaleTransition = !Motion.Enabled(Motion.Reduce) ? null : new Vector3Transition { Duration = TimeSpan.FromMilliseconds(160) };
            part.CenterPoint = new Vector3((float)x, (float)part.ActualHeight / 2, 0);
            part.Scale = new Vector3(scale, scale, 1);
        }
    }

    /// <summary>Corners for an option in a menu: the first follows the panel's top corners, the last its bottom ones.</summary>
    public static CornerRadius MenuCorners(bool first, bool last, double radius) =>
        new(first ? radius : 0, first ? radius : 0, last ? radius : 0, last ? radius : 0);
}
