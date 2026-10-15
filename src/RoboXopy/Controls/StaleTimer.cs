using System.Windows;
using System.Windows.Media;

namespace RoboXopy.Controls;

/// <summary>A small clockwise timer ring used to show how far a stale sync has progressed toward overdue.</summary>
public sealed class StaleTimer : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(StaleTimer),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IndicatorBrushProperty = DependencyProperty.Register(
        nameof(IndicatorBrush), typeof(Brush), typeof(StaleTimer),
        new FrameworkPropertyMetadata(Brushes.Goldenrod, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(StaleTimer),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public Brush IndicatorBrush { get => (Brush)GetValue(IndicatorBrushProperty); set => SetValue(IndicatorBrushProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = double.IsInfinity(availableSize.Width) || double.IsInfinity(availableSize.Height)
            ? 16
            : Math.Min(availableSize.Width, availableSize.Height);
        return new Size(size, size);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var diameter = Math.Min(ActualWidth, ActualHeight);
        if (diameter <= 0) return;

        var thickness = Math.Max(1.5, diameter / 7);
        var radius = Math.Max(0, (diameter - thickness) / 2);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var bounds = new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2);
        drawingContext.DrawEllipse(null, new Pen(TrackBrush, thickness), center, radius, radius);

        var progress = Math.Clamp(Progress, 0, 1);
        if (progress <= 0) return;
        if (progress >= 1)
        {
            drawingContext.DrawEllipse(null, new Pen(IndicatorBrush, thickness), center, radius, radius);
            return;
        }

        var start = new Point(center.X, center.Y - radius);
        var angle = progress * 360;
        var radians = (angle - 90) * Math.PI / 180;
        var end = new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
        var arc = new StreamGeometry();
        using (var context = arc.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, new Size(radius, radius), 0, angle > 180, SweepDirection.Clockwise, true, false);
        }

        arc.Freeze();
        drawingContext.DrawGeometry(null, new Pen(IndicatorBrush, thickness), arc);
    }
}
