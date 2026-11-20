using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace LoadLens.App.Controls;

public sealed record ChartSeries(string Name, Brush Stroke, IReadOnlyList<double> Values);

/// <summary>Multi-series 0..100 timeline chart with a fixed percentage grid.</summary>
public sealed class HistoryChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series),
        typeof(IEnumerable<ChartSeries>),
        typeof(HistoryChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(double),
        typeof(HistoryChart),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WindowMinutesProperty = DependencyProperty.Register(
        nameof(WindowMinutes),
        typeof(int),
        typeof(HistoryChart),
        new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<ChartSeries>? Series
    {
        get => (IEnumerable<ChartSeries>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public int WindowMinutes
    {
        get => (int)GetValue(WindowMinutesProperty);
        set => SetValue(WindowMinutesProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 20 || height < 20)
        {
            return;
        }

        var left = 34.0;
        var right = 8.0;
        var top = 8.0;
        var bottom = 20.0;
        var plotWidth = Math.Max(1, width - left - right);
        var plotHeight = Math.Max(1, height - top - bottom);

        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)), 1);
        gridPen.Freeze();
        var textBrush = new SolidColorBrush(Color.FromRgb(0x5F, 0x72, 0x87));
        textBrush.Freeze();

        for (var i = 0; i <= 4; i++)
        {
            var value = Maximum * i / 4.0;
            var y = top + plotHeight - (value / Maximum) * plotHeight;
            dc.DrawLine(gridPen, new Point(left, y), new Point(width - right, y));

            var label = new FormattedText(
                $"{value:0}%",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                9.5,
                textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(label, new Point(left - label.Width - 6, y - label.Height / 2));
        }

        var timeLabels = new[] { "начало", "1/3", "2/3", "сейчас" };
        for (var i = 0; i < 4; i++)
        {
            var x = left + plotWidth * i / 3.0;
            dc.DrawLine(gridPen, new Point(x, top), new Point(x, top + plotHeight));

            var label = new FormattedText(
                timeLabels[i],
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                9.5,
                textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var labelX = i == 0 ? x : i == 3 ? x - label.Width : x - label.Width / 2;
            dc.DrawText(label, new Point(labelX, top + plotHeight + 4));
        }

        var series = Series?.Where(s => s.Values.Count >= 2).ToArray();
        if (series is null || series.Length == 0)
        {
            var empty = new FormattedText(
                "Собираю историю…",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                12,
                textBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(
                empty,
                new Point(left + (plotWidth - empty.Width) / 2, top + (plotHeight - empty.Height) / 2));
            return;
        }

        foreach (var item in series)
        {
            var values = item.Values;
            var stepX = plotWidth / Math.Max(1, values.Count - 1);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(Map(values[0], 0, left, top, plotHeight, stepX), false, false);
                for (var i = 1; i < values.Count; i++)
                {
                    ctx.LineTo(Map(values[i], i, left, top, plotHeight, stepX), true, false);
                }
            }

            geometry.Freeze();
            var pen = new Pen(item.Stroke, 1.9)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    private Point Map(
        double value,
        int index,
        double left,
        double top,
        double plotHeight,
        double stepX)
    {
        var normalized = Math.Clamp(value / Maximum, 0, 1);
        return new Point(left + index * stepX, top + plotHeight - normalized * plotHeight);
    }
}
