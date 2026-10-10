using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using RoboXopy.Core.Formatting;
using RoboXopy.Core.Insights;
using RoboXopy.Core.Models;
using RoboXopy.Infrastructure;

namespace RoboXopy.Controls;

/// <summary>
/// Diverging bar chart of a profile's runs: what was copied above the baseline, what was removed below it,
/// the source size as a line on its own scale (right axis) and failed / unusual runs marked along the top.
/// Hover or arrow keys show the details of a run; click or Enter runs <see cref="ActivateCommand"/>.
/// </summary>
public sealed class RunHistoryChart : FrameworkElement
{
    private const double StatusLaneHeight = 18;
    private const double DateAxisHeight = 22;
    private const double AxisFontSize = 11;
    private const double AxisGap = 8;
    private const double MinBarHeight = 2;
    private const double MaxBarWidth = 28;
    private const double MinDateLabelSpacing = 90;

    /// <summary>Space above the tallest bar, as a fraction of the scale.</summary>
    private const double Headroom = 1.06;

    /// <summary>Distance of the break mark from the end of a bar that exceeds the scale.</summary>
    private const double BreakOffset = 10;

    /// <summary>Values above this many times the 90th percentile are outliers; the scale fits the rest.</summary>
    private const double OutlierFactor = 2.5;

    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<HistoryPoint>), typeof(RunHistoryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPointsChanged));

    public static readonly DependencyProperty MetricProperty = DependencyProperty.Register(
        nameof(Metric), typeof(HistoryMetric), typeof(RunHistoryChart),
        new FrameworkPropertyMetadata(HistoryMetric.Bytes, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(RunHistoryChart),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender, OnSelectedIndexChanged));

    public static readonly DependencyProperty ActivateCommandProperty = DependencyProperty.Register(
        nameof(ActivateCommand), typeof(ICommand), typeof(RunHistoryChart));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(RunHistoryChart));

    public static readonly DependencyProperty CopiedBrushProperty = RegisterBrush(nameof(CopiedBrush));
    public static readonly DependencyProperty RemovedBrushProperty = RegisterBrush(nameof(RemovedBrush));
    public static readonly DependencyProperty SourceBrushProperty = RegisterBrush(nameof(SourceBrush));
    public static readonly DependencyProperty GridBrushProperty = RegisterBrush(nameof(GridBrush));
    public static readonly DependencyProperty BaselineBrushProperty = RegisterBrush(nameof(BaselineBrush));
    public static readonly DependencyProperty AxisTextBrushProperty = RegisterBrush(nameof(AxisTextBrush));
    public static readonly DependencyProperty FailureBrushProperty = RegisterBrush(nameof(FailureBrush));
    public static readonly DependencyProperty WarningBrushProperty = RegisterBrush(nameof(WarningBrush));
    public static readonly DependencyProperty HighlightBrushProperty = RegisterBrush(nameof(HighlightBrush));
    public static readonly DependencyProperty FocusBrushProperty = RegisterBrush(nameof(FocusBrush));
    public static readonly DependencyProperty SurfaceBrushProperty = RegisterBrush(nameof(SurfaceBrush));

    private readonly ToolTip _tip = new() { Placement = PlacementMode.Relative };
    private ChartLayout? _layout;
    private int _hover = -1;
    private int _tipIndex = -1;
    private bool _focusVisible;

    static RunHistoryChart()
    {
        FocusableProperty.OverrideMetadata(typeof(RunHistoryChart), new FrameworkPropertyMetadata(true));
        FocusVisualStyleProperty.OverrideMetadata(typeof(RunHistoryChart), new FrameworkPropertyMetadata(null));
        KeyboardNavigation.IsTabStopProperty.OverrideMetadata(typeof(RunHistoryChart), new FrameworkPropertyMetadata(true));
    }

    public RunHistoryChart()
    {
        _tip.PlacementTarget = this;
        Unloaded += (_, _) => HideTip();
        IsVisibleChanged += (_, e) => { if (e.NewValue is false) HideTip(); };
    }

    public IReadOnlyList<HistoryPoint>? Points
    {
        get => (IReadOnlyList<HistoryPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public HistoryMetric Metric
    {
        get => (HistoryMetric)GetValue(MetricProperty);
        set => SetValue(MetricProperty, value);
    }

    /// <summary>Run selected with the keyboard (index into <see cref="Points"/>), or -1.</summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>Executed with the <see cref="RunRecord"/> of a clicked run.</summary>
    public ICommand? ActivateCommand
    {
        get => (ICommand?)GetValue(ActivateCommandProperty);
        set => SetValue(ActivateCommandProperty, value);
    }

    /// <summary>Text summary of the chart for screen readers.</summary>
    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public Brush CopiedBrush { get => (Brush)GetValue(CopiedBrushProperty); set => SetValue(CopiedBrushProperty, value); }
    public Brush RemovedBrush { get => (Brush)GetValue(RemovedBrushProperty); set => SetValue(RemovedBrushProperty, value); }
    public Brush SourceBrush { get => (Brush)GetValue(SourceBrushProperty); set => SetValue(SourceBrushProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public Brush BaselineBrush { get => (Brush)GetValue(BaselineBrushProperty); set => SetValue(BaselineBrushProperty, value); }
    public Brush AxisTextBrush { get => (Brush)GetValue(AxisTextBrushProperty); set => SetValue(AxisTextBrushProperty, value); }
    public Brush FailureBrush { get => (Brush)GetValue(FailureBrushProperty); set => SetValue(FailureBrushProperty, value); }
    public Brush WarningBrush { get => (Brush)GetValue(WarningBrushProperty); set => SetValue(WarningBrushProperty, value); }
    public Brush HighlightBrush { get => (Brush)GetValue(HighlightBrushProperty); set => SetValue(HighlightBrushProperty, value); }
    public Brush FocusBrush { get => (Brush)GetValue(FocusBrushProperty); set => SetValue(FocusBrushProperty, value); }

    /// <summary>Background the chart sits on; used to ring the highlighted point of the source line.</summary>
    public Brush SurfaceBrush { get => (Brush)GetValue(SurfaceBrushProperty); set => SetValue(SurfaceBrushProperty, value); }

    /// <summary>Text describing the selected run, read by screen readers as the item status.</summary>
    internal string? SelectedText =>
        Points is { } points && SelectedIndex >= 0 && SelectedIndex < points.Count ? DescribeRun(points[SelectedIndex]) : null;

    private static DependencyProperty RegisterBrush(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(RunHistoryChart),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (RunHistoryChart)d;
        chart._hover = -1;
        chart.HideTip();
        var count = (e.NewValue as IReadOnlyList<HistoryPoint>)?.Count ?? 0;
        if (chart.SelectedIndex >= count) chart.SelectedIndex = count - 1;
    }

    private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (RunHistoryChart)d;
        if (UIElementAutomationPeer.FromElement(chart) is { } peer)
        {
            peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.ItemStatusProperty, "", chart.SelectedText ?? "");
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new RunHistoryChartAutomationPeer(this);

    // ───────────────────────────── Rendering ─────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        _layout = null;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        var points = Points;
        if (points is null || points.Count == 0) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var px = new PixelSnapper(dpi.DpiScaleY);
        var typeface = new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        FormattedText Text(string s, Brush brush) =>
            new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, AxisFontSize, brush, dpi.PixelsPerDip);

        var metric = Metric;

        // Main (left) axis: copied above zero, removed below, one shared scale. Runs beyond the scale limit
        // are drawn as broken bars labelled with their value.
        var limit = ScaleLimit(points.Select(p => (double)p.Copied).Concat(points.Select(p => (double)p.Removed)));
        var maxUp = points.Select(p => (double)p.Copied).Where(v => v <= limit).DefaultIfEmpty(0).Max();
        var maxDown = points.Select(p => (double)p.Removed).Where(v => v <= limit).DefaultIfEmpty(0).Max();
        var upBroken = points.Any(p => p.Copied > limit);
        var downBroken = points.Any(p => p.Removed > limit);
        var hasData = maxUp + maxDown > 0 || upBroken || downBroken;
        var (step, unit) = ChooseStep(Math.Max(1, (Math.Max(maxUp, upBroken ? limit : 0) + maxDown) / 4.0), metric);
        var up = maxUp > 0 || upBroken ? Math.Max(Math.Max(maxUp, upBroken ? limit : 0), step) * Headroom : step;
        var down = maxDown > 0 || downBroken ? Math.Max(maxDown * Headroom, up * 0.25) : 0;

        var leftTicks = new List<(double Value, FormattedText Label)> { (0, Text("0", AxisTextBrush)) };
        if (hasData)
        {
            for (var v = step; v <= up * 1.001; v += step) leftTicks.Add((v, Text(FormatTick(v, unit, metric), AxisTextBrush)));
            for (var v = step; v <= down * 1.001; v += step) leftTicks.Add((-v, Text("\u2212" + FormatTick(v, unit, metric), AxisTextBrush)));
        }

        // Source line (right axis), mapped onto the upper region only.
        var sources = points.Select(p => p.SourceTotal).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        var showSource = sources.Count >= 2;
        double sourceLo = 0, sourceHi = 1;
        FormattedText? sourceLoLabel = null, sourceHiLabel = null;
        if (showSource)
        {
            (sourceLo, sourceHi, var sourceUnit) = SourceRange(sources.Min(), sources.Max(), metric);
            sourceLoLabel = Text(FormatTick(sourceLo, sourceUnit, metric), SourceBrush);
            sourceHiLabel = Text(FormatTick(sourceHi, sourceUnit, metric), SourceBrush);
        }

        var leftWidth = leftTicks.Max(t => t.Label.Width) + AxisGap;
        var rightWidth = showSource ? Math.Max(sourceLoLabel!.Width, sourceHiLabel!.Width) + AxisGap : 4;
        var layout = new ChartLayout(
            PlotLeft: px.Snap(leftWidth),
            PlotRight: px.Snap(width - rightWidth),
            PlotTop: StatusLaneHeight,
            PlotBottom: px.Snap(height - DateAxisHeight),
            Count: points.Count);
        if (layout.PlotWidth < 40 || layout.PlotHeight < 40) return;
        _layout = layout;

        var pxPerUnit = layout.PlotHeight / (up + down);
        var zero = px.Snap(layout.PlotTop + up * pxPerUnit);
        double ValueY(double v) => zero - v * pxPerUnit;

        // Highlight bands behind everything else.
        var selected = _focusVisible && IsKeyboardFocused ? SelectedIndex : -1;
        if (_hover >= 0 && _hover < points.Count) DrawBand(dc, layout, _hover, px, null);
        if (selected >= 0 && selected < points.Count)
        {
            DrawBand(dc, layout, selected, px, new Pen(FocusBrush, px.Line) { DashStyle = null });
        }

        // Grid.
        var gridPen = new Pen(GridBrush, px.Line);
        foreach (var (value, label) in leftTicks)
        {
            var y = px.Snap(ValueY(value));
            if (value != 0) dc.DrawLine(gridPen, new Point(layout.PlotLeft, y + px.Half), new Point(layout.PlotRight, y + px.Half));
            dc.DrawText(label, new Point(layout.PlotLeft - AxisGap / 2 - label.Width, y - label.Height / 2));
        }

        // Bars.
        var barWidth = layout.BarWidth;
        var radius = barWidth >= 6 ? 2 : 0;
        var clipped = new List<(double X, double Y, bool Up, double Value)>();
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var center = layout.SlotCenter(i);
            var left = px.Snap(center - barWidth / 2);
            var right = Math.Max(left + px.Pixel, px.Snap(center + barWidth / 2));

            if (p.Copied > 0)
            {
                var broken = p.Copied > limit;
                var top = broken ? layout.PlotTop : px.Snap(Math.Min(zero - MinBarHeight, ValueY(p.Copied)));
                DrawBar(dc, new Rect(left, top, right - left, zero - top), radius, up: true, CopiedBrush, p.IsPreview,
                    broken ? top + BreakOffset : null);
                if (broken) clipped.Add((center, top, true, p.Copied));
            }

            if (p.Removed > 0)
            {
                var broken = p.Removed > limit;
                var start = zero + px.Pixel;
                var bottom = broken ? layout.PlotBottom : px.Snap(Math.Max(start + MinBarHeight, ValueY(-p.Removed)));
                DrawBar(dc, new Rect(left, start, right - left, bottom - start), radius, up: false, RemovedBrush, p.IsPreview,
                    broken ? bottom - BreakOffset : null);
                if (broken) clipped.Add((center, bottom, false, p.Removed));
            }
        }

        dc.DrawLine(new Pen(BaselineBrush, px.Line), new Point(layout.PlotLeft, zero + px.Half), new Point(layout.PlotRight, zero + px.Half));

        // Values of the broken bars, beside the bar end.
        foreach (var (x, y, isUp, value) in clipped)
        {
            var label = Text(FormatValue(value, metric), AxisTextBrush);
            var pill = new Rect(0, 0, label.Width + 8, label.Height + 2);
            pill.X = x + barWidth / 2 + 3;
            if (pill.Right > layout.PlotRight) pill.X = x - barWidth / 2 - 3 - pill.Width;
            pill.Y = isUp ? y : y - pill.Height;
            dc.DrawRoundedRectangle(SurfaceBrush, null, pill, 3, 3);
            dc.DrawText(label, new Point(pill.X + 4, pill.Y + 1));
        }

        // Source line.
        if (showSource)
        {
            var top = layout.PlotTop;
            double SourceY(double v) => zero - (v - sourceLo) / (sourceHi - sourceLo) * (zero - top);

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                var started = false;
                for (var i = 0; i < points.Count; i++)
                {
                    if (points[i].SourceTotal is not { } v) continue;
                    var pt = new Point(layout.SlotCenter(i), SourceY(v));
                    if (!started) ctx.BeginFigure(pt, false, false);
                    else ctx.LineTo(pt, true, true);
                    started = true;
                }
            }

            geometry.Freeze();
            dc.DrawGeometry(null, new Pen(SourceBrush, 1.5) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);

            foreach (var i in new[] { _hover, selected }.Distinct())
            {
                if (i < 0 || i >= points.Count || points[i].SourceTotal is not { } v) continue;
                dc.DrawEllipse(SourceBrush, new Pen(SurfaceBrush, 1.5), new Point(layout.SlotCenter(i), SourceY(v)), 3.5, 3.5);
            }

            var x = layout.PlotRight + AxisGap / 2;
            dc.DrawText(sourceHiLabel!, new Point(x, top - sourceHiLabel!.Height / 2));
            dc.DrawText(sourceLoLabel!, new Point(x, zero - sourceLoLabel!.Height / 2));
        }

        // Status lane: failures and runs that need attention.
        var laneY = StatusLaneHeight / 2 - 1;
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var center = layout.SlotCenter(i);
            if (p.IsProblem)
            {
                dc.DrawEllipse(FailureBrush, null, new Point(center, laneY), 3.5, 3.5);
            }
            else if (p.IsWarning)
            {
                dc.DrawGeometry(WarningBrush, null, Triangle(center, laneY, 8));
            }
        }

        DrawDateLabels(dc, layout, points, Text, width);
    }

    private void DrawBand(DrawingContext dc, ChartLayout layout, int index, PixelSnapper px, Pen? outline)
    {
        var center = layout.SlotCenter(index);
        var bandWidth = Math.Max(Math.Min(layout.Slot, layout.BarWidth + 14), 3);
        var left = px.Snap(center - bandWidth / 2);
        var right = px.Snap(center + bandWidth / 2);
        var rect = new Rect(left, 0, right - left, layout.PlotBottom + 4);
        dc.DrawRoundedRectangle(HighlightBrush, null, rect, 4, 4);
        if (outline is not null)
        {
            rect.Inflate(-px.Half, -px.Half);
            dc.DrawRoundedRectangle(null, outline, rect, 4, 4);
        }
    }

    /// <param name="breakAt">Y at which to cut the bar with a double slash, showing it continues beyond the scale.</param>
    private static void DrawBar(DrawingContext dc, Rect rect, double radius, bool up, Brush brush, bool preview, double? breakAt)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        if (!preview)
        {
            dc.DrawGeometry(brush, null, Broken(BarGeometry(rect, radius, up), rect, breakAt));
        }
        else if (rect.Width < 5 || rect.Height < 4)
        {
            dc.PushOpacity(0.4);
            dc.DrawGeometry(brush, null, Broken(BarGeometry(rect, radius, up), rect, breakAt));
            dc.Pop();
        }
        else
        {
            var inner = rect;
            inner.Inflate(-0.75, -0.75);
            dc.PushOpacity(0.18);
            dc.DrawGeometry(brush, null, Broken(BarGeometry(rect, radius, up), rect, breakAt));
            dc.Pop();
            dc.DrawGeometry(null, new Pen(brush, 1.5), Broken(BarGeometry(inner, Math.Max(0, radius - 0.75), up), rect, breakAt));
        }
    }

    private static Geometry Broken(Geometry bar, Rect rect, double? breakAt)
    {
        if (breakAt is not { } y) return bar;

        const double gap = 2.5;
        var rise = Math.Min(5, rect.Width * 0.3);
        var cut = new StreamGeometry();
        using (var c = cut.Open())
        {
            foreach (var offset in new[] { -2.5, 2.5 })
            {
                var mid = y + offset;
                c.BeginFigure(new Point(rect.Left - 1, mid + rise / 2), true, true);
                c.LineTo(new Point(rect.Right + 1, mid - rise / 2), false, false);
                c.LineTo(new Point(rect.Right + 1, mid - rise / 2 + gap), false, false);
                c.LineTo(new Point(rect.Left - 1, mid + rise / 2 + gap), false, false);
            }
        }

        var result = new CombinedGeometry(GeometryCombineMode.Exclude, bar, cut);
        result.Freeze();
        return result;
    }

    private void DrawDateLabels(DrawingContext dc, ChartLayout layout, IReadOnlyList<HistoryPoint> points,
        Func<string, Brush, FormattedText> text, double width)
    {
        var first = points[0].Run.StartedAt.ToLocalTime();
        var last = points[^1].Run.StartedAt.ToLocalTime();
        var format = DateLabelFormat(first, last, CultureInfo.CurrentCulture);
        var every = Math.Max(1, (int)Math.Ceiling(MinDateLabelSpacing / layout.Slot));

        var lastRight = double.NegativeInfinity;
        string? lastText = null;
        for (var i = 0; i < points.Count; i += every)
        {
            var label = points[i].Run.StartedAt.ToLocalTime().ToString(format, CultureInfo.CurrentCulture);
            if (label == lastText) continue;
            var ft = text(label, AxisTextBrush);
            var x = Math.Clamp(layout.SlotCenter(i) - ft.Width / 2, 0, Math.Max(0, width - ft.Width));
            if (x < lastRight + 12) continue;
            dc.DrawText(ft, new Point(x, layout.PlotBottom + 6));
            lastRight = x + ft.Width;
            lastText = label;
        }
    }

    internal static string DateLabelFormat(DateTimeOffset first, DateTimeOffset last, CultureInfo culture)
    {
        var dtf = culture.DateTimeFormat;
        if (first.Date == last.Date) return dtf.ShortTimePattern;
        if (first.Year == last.Year) return dtf.MonthDayPattern.Replace("MMMM", "MMM");
        return dtf.YearMonthPattern.Replace("MMMM", "MMM");
    }

    private static Geometry BarGeometry(Rect r, double radius, bool up)
    {
        radius = Math.Min(radius, Math.Min(r.Width / 2, r.Height));
        if (radius < 0.5) return new RectangleGeometry(r);

        var g = new StreamGeometry();
        var corner = new Size(radius, radius);
        using (var c = g.Open())
        {
            if (up)
            {
                c.BeginFigure(new Point(r.Left, r.Bottom), true, true);
                c.LineTo(new Point(r.Left, r.Top + radius), true, false);
                c.ArcTo(new Point(r.Left + radius, r.Top), corner, 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(new Point(r.Right - radius, r.Top), true, false);
                c.ArcTo(new Point(r.Right, r.Top + radius), corner, 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(new Point(r.Right, r.Bottom), true, false);
            }
            else
            {
                c.BeginFigure(new Point(r.Left, r.Top), true, true);
                c.LineTo(new Point(r.Right, r.Top), true, false);
                c.LineTo(new Point(r.Right, r.Bottom - radius), true, false);
                c.ArcTo(new Point(r.Right - radius, r.Bottom), corner, 0, false, SweepDirection.Clockwise, true, false);
                c.LineTo(new Point(r.Left + radius, r.Bottom), true, false);
                c.ArcTo(new Point(r.Left, r.Bottom - radius), corner, 0, false, SweepDirection.Clockwise, true, false);
            }
        }

        g.Freeze();
        return g;
    }

    private static Geometry Triangle(double centerX, double centerY, double size)
    {
        var h = size * 0.866;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(centerX, centerY - h / 2 - 0.5), true, true);
            c.LineTo(new Point(centerX + size / 2, centerY + h / 2 - 0.5), false, false);
            c.LineTo(new Point(centerX - size / 2, centerY + h / 2 - 0.5), false, false);
        }

        g.Freeze();
        return g;
    }

    // ───────────────────────────── Scales ─────────────────────────────

    /// <summary>
    /// Largest value the bar scale shows. When a few runs dwarf the rest (a one-off mass copy or removal), a
    /// linear scale would flatten every other bar to nothing, so the scale stops at a few times the typical run
    /// and those runs are drawn broken, with their value.
    /// </summary>
    internal static double ScaleLimit(IEnumerable<double> values)
    {
        var sorted = values.Where(v => v > 0).Order().ToList();
        if (sorted.Count == 0) return 0;
        var max = sorted[^1];
        if (sorted.Count < 5) return max;

        var typical = sorted[(int)Math.Ceiling(sorted.Count * 0.9) - 1];
        var limit = sorted.Where(v => v <= typical * OutlierFactor).Max();
        return max > limit * 1.5 ? limit : max;
    }

    private static string FormatValue(double value, HistoryMetric metric) => metric == HistoryMetric.Bytes
        ? ByteSize.Format((long)value)
        : FormatTick(value, new AxisUnit(1, ""), metric) + " files";
    private readonly record struct AxisUnit(double Size, string Name);

    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>The 1-2-5 step nearest <paramref name="raw"/>; for bytes the unit is chosen first so labels stay in one unit.</summary>
    private static (double Step, AxisUnit Unit) ChooseStep(double raw, HistoryMetric metric)
    {
        if (metric == HistoryMetric.Files) return (Math.Max(1, NiceStep(raw)), new AxisUnit(1, ""));

        var k = 0;
        var size = 1.0;
        while (raw >= size * 1024 && k < ByteUnits.Length - 1)
        {
            size *= 1024;
            k++;
        }

        var inUnit = Math.Max(1, NiceStep(raw / size));
        if (inUnit > 512 && k < ByteUnits.Length - 1)
        {
            size *= 1024;
            k++;
            inUnit = 1;
        }

        return (inUnit * size, new AxisUnit(size, ByteUnits[k]));
    }

    private static double NiceStep(double raw)
    {
        if (raw <= 0 || double.IsNaN(raw)) return 1;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var f = raw / magnitude;
        var nice = f < 1.5 ? 1 : f < 3 ? 2 : f < 7 ? 5 : 10;
        return nice * magnitude;
    }

    /// <summary>
    /// Range for the source line. Starts at zero when the source varied a lot; otherwise zooms in, but never
    /// to less than 10% of its size so small changes don't look dramatic.
    /// </summary>
    private static (double Lo, double Hi, AxisUnit Unit) SourceRange(long min, long max, HistoryMetric metric)
    {
        if (max <= 0) return (0, 1, new AxisUnit(1, metric == HistoryMetric.Bytes ? "B" : ""));

        if (min < max * 0.5)
        {
            var (s, u) = ChooseStep(max / 2.0, metric);
            return (0, Math.Max(s, Math.Ceiling(max / s) * s), u);
        }

        var range = Math.Max(max - min, max * 0.1);
        var mid = (min + max) / 2.0;
        var (step, unit) = ChooseStep(range / 2, metric);
        var lo = Math.Max(0, Math.Floor((mid - range * 0.6) / step) * step);
        var hi = Math.Ceiling((mid + range * 0.6) / step) * step;
        if (hi <= lo) hi = lo + step;
        return (lo, hi, unit);
    }

    private static string FormatTick(double value, AxisUnit unit, HistoryMetric metric)
    {
        if (value == 0) return "0";
        var culture = CultureInfo.CurrentCulture;
        if (metric == HistoryMetric.Bytes) return string.Create(culture, $"{value / unit.Size:0.#} {unit.Name}");
        return value switch
        {
            < 10_000 => value.ToString("N0", culture),
            < 1_000_000 => string.Create(culture, $"{value / 1_000:0.#}K"),
            < 1_000_000_000 => string.Create(culture, $"{value / 1_000_000:0.#}M"),
            _ => string.Create(culture, $"{value / 1_000_000_000:0.#}B"),
        };
    }

    // ───────────────────────────── Interaction ─────────────────────────────

    private int IndexAt(Point p)
    {
        if (_layout is not { } l || Points is not { Count: > 0 } points) return -1;
        if (p.X < l.PlotLeft || p.X >= l.PlotRight || p.Y < 0 || p.Y > l.PlotBottom + DateAxisHeight) return -1;
        return Math.Clamp((int)((p.X - l.PlotLeft) / l.Slot), 0, points.Count - 1);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetHover(IndexAt(e.GetPosition(this)));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        SetHover(-1);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var index = IndexAt(e.GetPosition(this));
        if (index < 0) return;
        _focusVisible = false;
        Focus();
        SelectedIndex = index;
        Activate(index);
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        _focusVisible = InputManager.Current.MostRecentInputDevice is KeyboardDevice;
        if (_focusVisible && Points is { Count: > 0 } points)
        {
            if (SelectedIndex < 0 || SelectedIndex >= points.Count) SelectedIndex = points.Count - 1;
            ShowTip(SelectedIndex);
        }

        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        _focusVisible = false;
        if (_hover < 0) HideTip();
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Points is not { Count: > 0 } points) return;

        var index = SelectedIndex < 0 ? points.Count - 1 : SelectedIndex;
        switch (e.Key)
        {
            case Key.Left: index = Math.Max(0, index - 1); break;
            case Key.Right: index = Math.Min(points.Count - 1, index + 1); break;
            case Key.Home: index = 0; break;
            case Key.End: index = points.Count - 1; break;
            case Key.Enter:
            case Key.Space:
                Activate(index);
                e.Handled = true;
                return;
            case Key.Escape:
                if (_tip.IsOpen)
                {
                    HideTip();
                    e.Handled = true;
                }

                return;
            default:
                return;
        }

        _focusVisible = true;
        _hover = -1;
        SelectedIndex = index;
        ShowTip(index);
        InvalidateVisual();
        e.Handled = true;
    }

    private void SetHover(int index)
    {
        if (index == _hover) return;
        _hover = index;
        Cursor = index >= 0 && ActivateCommand is not null ? Cursors.Hand : null;
        if (index >= 0) ShowTip(index);
        else HideTip();
        InvalidateVisual();
    }

    private void Activate(int index)
    {
        if (Points is not { } points || index < 0 || index >= points.Count) return;
        var run = points[index].Run;
        if (ActivateCommand is { } command && command.CanExecute(run)) command.Execute(run);
    }

    private void ShowTip(int index)
    {
        if (_layout is not { } l || Points is not { } points || index < 0 || index >= points.Count) return;

        if (index != _tipIndex || _tip.Content is null)
        {
            _tip.Content = BuildTip(points[index], ActivateCommand is not null);
            _tipIndex = index;
        }

        var content = (FrameworkElement)_tip.Content;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var tipWidth = content.DesiredSize.Width + 24;

        var center = l.SlotCenter(index);
        var half = Math.Max(Math.Min(l.Slot, l.BarWidth + 14), 3) / 2;
        var x = center + half + 8;
        if (x + tipWidth > ActualWidth && center - half - 8 - tipWidth >= 0) x = center - half - 8 - tipWidth;

        _tip.HorizontalOffset = x;
        _tip.VerticalOffset = l.PlotTop;
        _tip.IsOpen = true;
    }

    private void HideTip()
    {
        _tip.IsOpen = false;
        _tipIndex = -1;
        _tip.Content = null;
    }

    // ───────────────────────────── Text ─────────────────────────────

    private static FrameworkElement BuildTip(HistoryPoint point, bool canActivate)
    {
        var run = point.Run;
        var panel = new StackPanel { MaxWidth = 320 };
        panel.Children.Add(new TextBlock { Text = FormatWhen(run.StartedAt), FontWeight = FontWeights.SemiBold });
        panel.Children.Add(Secondary($"{RunFormatting.OutcomeTitle(run.Outcome, run.DryRun)} · {RunFormatting.TriggerText(run.Trigger)}"));

        if (run.Summary is { } s)
        {
            var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddRow(grid, run.DryRun ? "To copy" : "Copied", Amount(s.Bytes.Copied, s.Files.Copied));
            if (s.Files.Extras > 0 || s.Bytes.Extras > 0)
            {
                var label = !run.PurgesExtras ? "Extra (kept)" : run.DryRun ? "To remove" : "Removed";
                AddRow(grid, label, Amount(s.Bytes.Extras, s.Files.Extras));
            }

            if (s.Files.Failed > 0) AddRow(grid, "Failed", Files(s.Files.Failed));
            AddRow(grid, "Source", Amount(s.Bytes.Total, s.Files.Total));
            if (run.EndedAt > run.StartedAt) AddRow(grid, "Duration", RunFormatting.FormatDuration(run.Duration));
            panel.Children.Add(grid);
        }
        else if (!string.IsNullOrWhiteSpace(run.Message))
        {
            var message = Secondary(run.Message);
            message.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(message);
        }

        foreach (var finding in run.Findings ?? [])
        {
            var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var icon = new TextBlock
            {
                Text = finding.Severity == FindingSeverity.Warning ? "\uE7BA" : "\uE946",
                FontSize = 14,
                Margin = new Thickness(0, 1, 8, 0),
                VerticalAlignment = VerticalAlignment.Top,
            };
            icon.SetResourceReference(TextBlock.FontFamilyProperty, "SymbolThemeFontFamily");
            icon.SetResourceReference(TextBlock.ForegroundProperty,
                finding.Severity == FindingSeverity.Warning ? "SystemFillColorCautionBrush" : "TextFillColorSecondaryBrush");
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = finding.Message, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(row);
        }

        if (canActivate)
        {
            var hint = Secondary("Click to find this run in the list");
            hint.Margin = new Thickness(0, 8, 0, 0);
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
            panel.Children.Add(hint);
        }

        return panel;
    }

    private static void AddRow(Grid grid, string label, string value)
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var l = Secondary(label);
        l.Margin = new Thickness(0, 1, 16, 1);
        Grid.SetRow(l, row);
        var v = new TextBlock { Text = value, Margin = new Thickness(0, 1, 0, 1) };
        Grid.SetRow(v, row);
        Grid.SetColumn(v, 1);
        grid.Children.Add(l);
        grid.Children.Add(v);
    }

    private static TextBlock Secondary(string text)
    {
        var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        return tb;
    }

    private static string Files(long count) =>
        string.Create(CultureInfo.CurrentCulture, $"{count:N0} {(count == 1 ? "file" : "files")}");

    private static string Amount(long bytes, long files) => $"{ByteSize.Format(bytes)} · {Files(files)}";

    private static string FormatWhen(DateTimeOffset when)
    {
        var local = when.ToLocalTime();
        var culture = CultureInfo.CurrentCulture;
        var monthDay = culture.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM");
        var date = local.ToString($"ddd, {monthDay}", culture);
        if (local.Year != DateTimeOffset.Now.Year) date += local.ToString(" yyyy", culture);
        return $"{date} · {local.ToString(culture.DateTimeFormat.ShortTimePattern, culture)}";
    }

    internal static string DescribeRun(HistoryPoint point)
    {
        var run = point.Run;
        var parts = new List<string> { FormatWhen(run.StartedAt), RunFormatting.OutcomeTitle(run.Outcome, run.DryRun) };
        if (run.Summary is { } s)
        {
            parts.Add($"{(run.DryRun ? "to copy" : "copied")} {Amount(s.Bytes.Copied, s.Files.Copied)}");
            if (run.PurgesExtras && s.Files.Extras > 0) parts.Add($"{(run.DryRun ? "to remove" : "removed")} {Amount(s.Bytes.Extras, s.Files.Extras)}");
            parts.Add($"source {Amount(s.Bytes.Total, s.Files.Total)}");
        }
        else if (!string.IsNullOrWhiteSpace(run.Message))
        {
            parts.Add(run.Message);
        }

        parts.AddRange(run.Findings?.Select(f => f.Message) ?? []);
        return string.Join(", ", parts);
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    private sealed record ChartLayout(double PlotLeft, double PlotRight, double PlotTop, double PlotBottom, int Count)
    {
        public double PlotWidth => PlotRight - PlotLeft;
        public double PlotHeight => PlotBottom - PlotTop;
        public double Slot => PlotWidth / Count;
        public double BarWidth => Math.Clamp(Slot * 0.62, 1, MaxBarWidth);
        public double SlotCenter(int index) => PlotLeft + (index + 0.5) * Slot;
    }

    /// <summary>Rounds coordinates to device pixels so 1px lines and bar edges stay crisp at any DPI.</summary>
    private readonly struct PixelSnapper(double scale)
    {
        public double Pixel => 1 / scale;
        public double Half => 0.5 / scale;
        public double Line => 1 / scale;
        public double Snap(double value) => Math.Round(value * scale) / scale;
    }

    private sealed class RunHistoryChartAutomationPeer(RunHistoryChart owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override string GetClassNameCore() => nameof(RunHistoryChart);
        protected override bool IsKeyboardFocusableCore() => true;
        protected override bool IsContentElementCore() => true;

        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            return string.IsNullOrEmpty(name) ? "Activity chart" : name;
        }

        protected override string GetHelpTextCore()
        {
            var help = base.GetHelpTextCore();
            return string.IsNullOrEmpty(help) ? owner.Description ?? "" : help;
        }

        protected override string GetItemStatusCore() => owner.SelectedText ?? base.GetItemStatusCore();
    }
}
