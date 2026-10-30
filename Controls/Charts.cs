using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MeuOrcamento.Controls;

/// <summary>Base for the custom drawings: picks up the palette brushes (Themes/Dark.xaml).</summary>
public abstract class ChartElement : FrameworkElement
{
    public static readonly DependencyProperty InkProperty = RegisterBrush("Ink");
    public static readonly DependencyProperty Ink2Property = RegisterBrush("Ink2");
    public static readonly DependencyProperty Ink3Property = RegisterBrush("Ink3");
    public static readonly DependencyProperty OutlineProperty = RegisterBrush("Outline");
    public static readonly DependencyProperty AlertColorProperty = RegisterBrush("AlertColor");

    static DependencyProperty RegisterBrush(string name) => DependencyProperty.Register(name, typeof(Brush), typeof(ChartElement),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    protected ChartElement()
    {
        SetResourceReference(InkProperty, "Ink");
        SetResourceReference(Ink2Property, "Ink2");
        SetResourceReference(Ink3Property, "Ink3");
        SetResourceReference(OutlineProperty, "RuleStrong");
        SetResourceReference(AlertColorProperty, "Alert");
        SnapsToDevicePixels = true;
    }

    protected Brush Ink => (Brush)GetValue(InkProperty);
    protected Brush Ink2 => (Brush)GetValue(Ink2Property);
    protected Brush Ink3 => (Brush)GetValue(Ink3Property);
    protected Brush Outline => (Brush)GetValue(OutlineProperty);
    protected Brush AlertColor => (Brush)GetValue(AlertColorProperty);

    /// <summary>45° hatching, used for what has no destination.</summary>
    protected static Brush Hatch(Brush ink)
    {
        var lines = new GeometryGroup();
        lines.Children.Add(new LineGeometry(new Point(0, 6), new Point(6, 0)));
        lines.Children.Add(new LineGeometry(new Point(-1, 1), new Point(1, -1)));
        lines.Children.Add(new LineGeometry(new Point(5, 7), new Point(7, 5)));
        var brush = new DrawingBrush(new GeometryDrawing(null, new Pen(ink, 1), lines))
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 6, 6),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 6, 6),
            ViewboxUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>Chart text: same mono and same size as the labels (scale in Themes/Styles.xaml).</summary>
    protected void DrawText(DrawingContext dc, string text, double x, double y, double width, Brush ink,
                            TextAlignment alignment = TextAlignment.Left)
    {
        if (string.IsNullOrEmpty(text) || width < 14) return;
        var mono = (FontFamily)FindResource("Mono");
        var size = (double)FindResource("SizeLabel");
        var ft = new FormattedText(text, Format.PtBr, FlowDirection.LeftToRight, new Typeface(mono, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            size, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = width,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment,
        };
        dc.DrawText(ft, new Point(x, y));
    }
}

public record StripSegment(Guid? Id, string Name, double Fraction, string Detail, string Status, bool Alert,
                           double Usage, bool Hatched, string Tooltip, Brush? Color = null);

/// <summary>
/// The salary as a strip: each stretch is a share (width = % of income),
/// the filled part is what was already posted, the hatched part is what has no destination.
/// </summary>
public class StripChart : ChartElement
{
    const double Gap = 6, YName = 0, YDetail = 16, YBar = 40, BarHeight = 12, YStatus = 60;

    IReadOnlyList<StripSegment> _segments = Array.Empty<StripSegment>();
    readonly List<Rect> _areas = new();
    readonly ToolTip _tooltip = new() { Placement = PlacementMode.Relative };
    int _hover = -1, _pressed = -1;

    public event Action<Guid>? SegmentClicked;

    public static readonly DependencyProperty ShowStatusProperty = DependencyProperty.Register(nameof(ShowStatus), typeof(bool), typeof(StripChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
            (d, _) => ((StripChart)d).Height = ((StripChart)d).ShowStatus ? 108 : 84));

    /// <summary>Shows the status line (free, missing, over) under each stretch.</summary>
    public bool ShowStatus { get => (bool)GetValue(ShowStatusProperty); set => SetValue(ShowStatusProperty, value); }

    double YScale => ShowStatus ? 86 : 62;

    public StripChart()
    {
        Height = 108;
        _tooltip.PlacementTarget = this;
    }

    public void SetSegments(IEnumerable<StripSegment> segments)
    {
        _segments = segments.ToList();
        _hover = -1;
        _tooltip.IsOpen = false;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        _areas.Clear();
        double width = ActualWidth;
        if (width <= 0) return;

        double sum = _segments.Sum(s => Math.Max(0, s.Fraction));
        int visible = _segments.Count(s => s.Fraction > 0);
        double usable = width - Gap * Math.Max(0, visible - 1);
        double scale = Math.Max(1, sum); // over 100%: squeeze to fit
        var outline = new Pen(Outline, 1);
        var rule = new Pen(Outline, 1);
        // where the strip ends; the final label ("100%") takes the last ~56px
        double expectedEnd = Math.Min(width, sum / scale * usable + Gap * Math.Max(0, visible - 1));
        const double RuleLabelWidth = 48;

        double x = 0, accumulated = 0;
        for (int i = 0; i < _segments.Count; i++)
        {
            var s = _segments[i];
            if (s.Fraction <= 0) { _areas.Add(Rect.Empty); continue; }
            double w = s.Fraction / scale * usable;
            _areas.Add(new Rect(x, 0, w, YScale));
            bool hover = i == _hover;

            // the category color goes in a small square before the name and in the fill
            double indent = 0;
            if (s.Color != null && w >= 24)
            {
                dc.DrawRectangle(s.Color, null, new Rect(x, YName + 3, 7, 7));
                indent = 12;
            }
            DrawText(dc, s.Name, x + indent, YName, w - 4 - indent, hover ? Ink : Ink2);
            DrawText(dc, s.Detail, x, YDetail, w - 4, Ink3);

            var bar = new Rect(x + 0.5, YBar + 0.5, Math.Max(0, w - 1), BarHeight - 1);
            if (s.Hatched)
            {
                dc.DrawRectangle(Hatch(Ink3), outline, bar);
            }
            else
            {
                dc.DrawRectangle(null, hover ? new Pen(Ink2, 1) : outline, bar);
                double usage = Math.Clamp(s.Usage, 0, 1);
                if (usage > 0) dc.DrawRectangle(s.Color ?? Ink, null, new Rect(x, YBar, w * usage, BarHeight));
                // overspend marker: a square in the alert color, at the end of the stretch
                if (s.Alert) dc.DrawRectangle(AlertColor, null, new Rect(x + w - 6, YBar - 9, 6, 6));
            }
            if (ShowStatus) DrawText(dc, s.Status, x, YStatus, w - 4, s.Alert ? AlertColor : Ink3);

            // ruler: a tick at the start of each stretch, with the running total
            dc.DrawLine(rule, new Point(x + 0.5, YScale), new Point(x + 0.5, YScale + 5));
            bool fitsBeforeEnd = x + 4 + RuleLabelWidth <= expectedEnd - RuleLabelWidth;
            if ((w >= RuleLabelWidth || i == 0) && fitsBeforeEnd)
                DrawText(dc, Format.Percent((decimal)Math.Round(accumulated * 100, 1)), x + 4, YScale + 4, Math.Max(w - 6, RuleLabelWidth), Ink3);
            accumulated += s.Fraction;
            x += w + Gap;
        }

        double end = Math.Min(width, x - Gap);
        if (visible > 0)
        {
            dc.DrawLine(rule, new Point(0, YScale + 0.5), new Point(end, YScale + 0.5));
            dc.DrawLine(rule, new Point(end - 0.5, YScale), new Point(end - 0.5, YScale + 5));
            DrawText(dc, Format.Percent((decimal)Math.Round(accumulated * 100, 1)), end - 60, YScale + 4, 56, Ink3, TextAlignment.Right);
        }
    }

    int FindSegment(Point p)
    {
        for (int i = 0; i < _areas.Count; i++)
            if (!_areas[i].IsEmpty && _areas[i].Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int found = FindSegment(e.GetPosition(this));
        Cursor = found >= 0 && _segments[found].Id != null ? Cursors.Hand : Cursors.Arrow;
        if (found == _hover) return;
        _hover = found;
        if (found >= 0)
        {
            _tooltip.Content = _segments[found].Tooltip;
            _tooltip.HorizontalOffset = _areas[found].X;
            _tooltip.VerticalOffset = YScale + 22;
            _tooltip.IsOpen = true;
        }
        else _tooltip.IsOpen = false;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        _tooltip.IsOpen = false;
        InvalidateVisual();
    }

    // a click only counts when pressed and released on the same stretch (releasing a press that started elsewhere doesn't)
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _pressed = FindSegment(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        int released = FindSegment(e.GetPosition(this));
        bool sameStretch = released >= 0 && released == _pressed;
        _pressed = -1;
        if (sameStretch && _segments[released].Id is Guid id) SegmentClicked?.Invoke(id);
    }
}

/// <summary>Thin progress rule: a 1px track, filled at 3px.</summary>
public class ProgressLine : ChartElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(ProgressLine),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AlertProperty = DependencyProperty.Register(nameof(Alert), typeof(bool), typeof(ProgressLine),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(Brush), typeof(ProgressLine),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool Alert { get => (bool)GetValue(AlertProperty); set => SetValue(AlertProperty, value); }

    /// <summary>Fill color (the category's); without it, the theme ink is used.</summary>
    public Brush? Color { get => (Brush?)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    public ProgressLine() => Height = 3;

    protected override void OnRender(DrawingContext dc)
    {
        double middle = Math.Round(ActualHeight / 2);
        dc.DrawRectangle(Outline, null, new Rect(0, middle - 0.5, ActualWidth, 1));
        double v = Math.Clamp(Value, 0, 1);
        if (v > 0) dc.DrawRectangle(Alert ? AlertColor : Color ?? Ink, null, new Rect(0, middle - 1.5, v * ActualWidth, 3));
    }
}

/// <summary>A solid stretch of a composition bar, as a fraction of its width.</summary>
public record BarPart(double Fraction, Brush Fill);

/// <summary>How a month's income was used: one stretch per category, in its color, then what's free (hatched).</summary>
public class CompositionBar : ChartElement
{
    public static readonly DependencyProperty PartsProperty = DependencyProperty.Register(nameof(Parts), typeof(IReadOnlyList<BarPart>), typeof(CompositionBar),
        new FrameworkPropertyMetadata(Array.Empty<BarPart>(), FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FreeProperty = DependencyProperty.Register(nameof(Free), typeof(double), typeof(CompositionBar),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The spent and invested stretches, in drawing order.</summary>
    public IReadOnlyList<BarPart> Parts { get => (IReadOnlyList<BarPart>)GetValue(PartsProperty); set => SetValue(PartsProperty, value); }
    public double Free { get => (double)GetValue(FreeProperty); set => SetValue(FreeProperty, value); }

    public CompositionBar() => Height = 10;

    protected override void OnRender(DrawingContext dc)
    {
        // transparent background, so the tooltip shows anywhere on the bar, not just over the stretches
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        double w = ActualWidth, h = ActualHeight, x = 0;
        void Part(double fraction, Brush? fill, bool hatched)
        {
            double width = Math.Clamp(fraction, 0, 1) * w;
            if (width < 1) return;
            var r = new Rect(x, 0, Math.Max(0, width - 2), h);
            if (hatched) dc.DrawRectangle(Hatch(Ink3), new Pen(Outline, 1), new Rect(r.X + 0.5, 0.5, Math.Max(0, r.Width - 1), h - 1));
            else dc.DrawRectangle(fill, null, r);
            x += width;
        }
        foreach (var part in Parts) Part(part.Fraction, part.Fill, false);
        Part(Free, null, true);
    }
}
