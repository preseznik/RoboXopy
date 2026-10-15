using System.Globalization;
using System.Windows;
using System.Windows.Media;
using RoboXopy.Infrastructure;

namespace RoboXopy.Controls;

public enum BreakdownKind
{
    Unchanged,
    Copied,
    Removed,
    Kept,
    Mismatch,
    Failed,
}

/// <summary>One part of a run's file breakdown: what happened (or would happen) to how many files.</summary>
public sealed record BreakdownSegment(BreakdownKind Kind, long Count, long Bytes, string Label)
{
    public string CountText => Count.ToString("N0", CultureInfo.CurrentCulture);
    public string? BytesText => Bytes > 0 ? RunFormatting.FormatBytes(Bytes) : null;
    public string BytesSuffix => Bytes > 0 ? " · " + RunFormatting.FormatBytes(Bytes) : string.Empty;
    public bool HasBytes => Bytes > 0;
    public bool CanFilter => Kind is BreakdownKind.Unchanged or BreakdownKind.Copied or BreakdownKind.Removed or BreakdownKind.Kept;
}

/// <summary>
/// A single stacked bar of a run's files, sized by count.
/// Every non-empty segment stays visible, however small, so a handful of removals next to a million
/// unchanged files still shows up. The legend values below the bar are clickable to filter output.
/// </summary>
public sealed class BreakdownBar : FrameworkElement
{
    private const double BarHeight = 8;
    private const double MinSegmentWidth = 6;
    private const double Gap = 2;

    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IReadOnlyList<BreakdownSegment>), typeof(BreakdownBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedKindProperty = DependencyProperty.Register(
        nameof(SelectedKind), typeof(BreakdownKind?), typeof(BreakdownBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnchangedBrushProperty = RegisterBrush(nameof(UnchangedBrush));
    public static readonly DependencyProperty CopiedBrushProperty = RegisterBrush(nameof(CopiedBrush));
    public static readonly DependencyProperty RemovedBrushProperty = RegisterBrush(nameof(RemovedBrush));
    public static readonly DependencyProperty KeptBrushProperty = RegisterBrush(nameof(KeptBrush));
    public static readonly DependencyProperty MismatchBrushProperty = RegisterBrush(nameof(MismatchBrush));
    public static readonly DependencyProperty FailedBrushProperty = RegisterBrush(nameof(FailedBrush));

    public IReadOnlyList<BreakdownSegment>? Segments
    {
        get => (IReadOnlyList<BreakdownSegment>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public BreakdownKind? SelectedKind
    {
        get => (BreakdownKind?)GetValue(SelectedKindProperty);
        set => SetValue(SelectedKindProperty, value);
    }

    public Brush? UnchangedBrush { get => (Brush?)GetValue(UnchangedBrushProperty); set => SetValue(UnchangedBrushProperty, value); }
    public Brush? CopiedBrush { get => (Brush?)GetValue(CopiedBrushProperty); set => SetValue(CopiedBrushProperty, value); }
    public Brush? RemovedBrush { get => (Brush?)GetValue(RemovedBrushProperty); set => SetValue(RemovedBrushProperty, value); }
    public Brush? KeptBrush { get => (Brush?)GetValue(KeptBrushProperty); set => SetValue(KeptBrushProperty, value); }
    public Brush? MismatchBrush { get => (Brush?)GetValue(MismatchBrushProperty); set => SetValue(MismatchBrushProperty, value); }
    public Brush? FailedBrush { get => (Brush?)GetValue(FailedBrushProperty); set => SetValue(FailedBrushProperty, value); }

    public BreakdownBar()
    {
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, BarHeight);

    protected override void OnRender(DrawingContext dc)
    {
        var segments = Segments?.Where(s => s.Count > 0).ToList();
        var width = ActualWidth;
        var height = ActualHeight;
        if (segments is not { Count: > 0 } || width <= 0 || height <= 0) return;

        var widths = Layout(segments.Select(s => (double)s.Count).ToList(), width - Gap * (segments.Count - 1), MinSegmentWidth);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height), height / 2, height / 2));
        var x = 0.0;
        for (var i = 0; i < segments.Count; i++)
        {
            // The last segment absorbs rounding so the bar always ends flush.
            var right = i == segments.Count - 1 ? width : Math.Round(x + widths[i]);
            if (right > x)
            {
                var kind = segments[i].Kind;
                var brush = BrushFor(kind);
                
                // Apply selection highlighting: slightly brighter
                if (SelectedKind == kind && brush is SolidColorBrush scb)
                {
                    var c = scb.Color;
                    var brightenedColor = Color.FromArgb(
                        c.A,
                        (byte)Math.Min(255, c.R + 40),
                        (byte)Math.Min(255, c.G + 40),
                        (byte)Math.Min(255, c.B + 40)
                    );
                    brush = new SolidColorBrush(brightenedColor);
                }
                
                dc.DrawRectangle(brush, null, new Rect(x, 0, right - x, height));
            }
            x = right + Gap;
        }

        dc.Pop();
    }

    /// <summary>Splits <paramref name="width"/> in proportion to <paramref name="values"/>, giving each at least <paramref name="min"/>.</summary>
    internal static double[] Layout(IReadOnlyList<double> values, double width, double min)
    {
        var n = values.Count;
        var result = new double[n];
        if (n == 0 || width <= 0) return result;

        min = Math.Min(min, width / n);
        var pinned = new bool[n];
        var pinnedCount = 0;
        while (true)
        {
            var free = width - pinnedCount * min;
            var sum = 0.0;
            for (var i = 0; i < n; i++) if (!pinned[i]) sum += values[i];

            var changed = false;
            for (var i = 0; i < n; i++)
            {
                if (pinned[i]) continue;
                result[i] = sum > 0 ? values[i] / sum * free : free / (n - pinnedCount);
                if (result[i] < min)
                {
                    pinned[i] = true;
                    pinnedCount++;
                    changed = true;
                }
            }

            if (!changed) break;
        }

        for (var i = 0; i < n; i++) if (pinned[i]) result[i] = min;
        return result;
    }

    private Brush? BrushFor(BreakdownKind kind) => kind switch
    {
        BreakdownKind.Unchanged => UnchangedBrush,
        BreakdownKind.Copied => CopiedBrush,
        BreakdownKind.Removed => RemovedBrush,
        BreakdownKind.Kept => KeptBrush,
        BreakdownKind.Mismatch => MismatchBrush,
        _ => FailedBrush,
    };

    private static DependencyProperty RegisterBrush(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(BreakdownBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
}
