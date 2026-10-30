using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MeuOrcamento;

/// <summary>Each category's color (tied to it by the number stored in Category.Color).</summary>
public static class CategoryColors
{
    /// <summary>How many distinct colors exist; it's also the limit of categories per month.</summary>
    public const int Count = 7;

    public static Brush For(int slot) =>
        (Brush)Application.Current.FindResource($"Category{Math.Clamp(slot, 0, Count - 1)}");

    /// <summary>The small square-cornered marker that goes next to the category name.</summary>
    public static Rectangle Marker(int slot, double size = 8) => new()
    {
        Width = size,
        Height = size,
        Fill = For(slot),
        VerticalAlignment = VerticalAlignment.Center,
        SnapsToDevicePixels = true,
    };
}
