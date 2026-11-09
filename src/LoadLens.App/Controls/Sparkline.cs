using System.Windows;
using System.Windows.Media;

namespace LoadLens.App.Controls;

/// <summary>A tiny dependency-free sparkline used on the metric cards.</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values),
        typeof(IEnumerable<double>),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill),
        typeof(Brush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<double>? Values
    {
        get => (IEnumerable<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 1 || height <= 1)
        {
            return;
        }

        var values = Values?.Where(double.IsFinite).ToArray();
        if (values is null || values.Length < 2)
        {
            drawingContext.DrawLine(
                new Pen(new SolidColorBrush(Color.FromArgb(34, 255, 255, 255)), 1),
                new Point(0, height - 1),
                new Point(width, height - 1));
            return;
        }

        var max = Math.Max(1, Maximum);
        var stepX = width / (values.Length - 1);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var first = Map(values[0], stepX, height, max);
            ctx.BeginFigure(new Point(0, height), true, true);
            ctx.LineTo(first, true, false);

            for (var i = 1; i < values.Length; i++)
            {
                ctx.LineTo(Map(values[i], i * stepX, height, max), true, false);
            }

            ctx.LineTo(new Point(width, height), true, false);
        }

        geometry.Freeze();

        if (Fill is not null)
        {
            drawingContext.DrawGeometry(Fill, null, geometry);
        }

        var strokeGeometry = new StreamGeometry();
        using (var ctx = strokeGeometry.Open())
        {
            ctx.BeginFigure(Map(values[0], 0, height, max), false, false);
            for (var i = 1; i < values.Length; i++)
            {
                ctx.LineTo(Map(values[i], i * stepX, height, max), true, false);
            }
        }

        strokeGeometry.Freeze();
        var pen = new Pen(Stroke, 1.7)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();
        drawingContext.DrawGeometry(null, pen, strokeGeometry);
    }

    private static Point Map(double value, double x, double height, double max)
    {
        var normalized = Math.Clamp(value / max, 0, 1);
        return new Point(x, height - 1 - normalized * (height - 2));
    }
}
