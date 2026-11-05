using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace ClaudeTimer.Controls;

/// <summary>
/// Måler med en 240°-bue: forbruget i niveaufarven, en svag bue frem til
/// prognosen og en markør ved prognosen (klemt til 100 %).
/// </summary>
public sealed class UsageGauge : FrameworkElement
{
    private const double StartAngle = 150;
    private const double SweepAngle = 240;

    public static readonly DependencyProperty ValueProperty = Register(nameof(Value), 0d);
    public static readonly DependencyProperty ForecastProperty = Register(nameof(Forecast), double.NaN);
    public static readonly DependencyProperty ThicknessProperty = Register(nameof(Thickness), 12d);
    public static readonly DependencyProperty TrackBrushProperty = Register<Brush?>(nameof(TrackBrush), null);
    public static readonly DependencyProperty ValueBrushProperty = Register<Brush?>(nameof(ValueBrush), null);
    public static readonly DependencyProperty ForecastBrushProperty = Register<Brush?>(nameof(ForecastBrush), null);

    /// <summary>Forbrug i procent (0–100).</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Forventet forbrug ved nulstilling; NaN skjuler prognosen.</summary>
    public double Forecast
    {
        get => (double)GetValue(ForecastProperty);
        set => SetValue(ForecastProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush? ValueBrush
    {
        get => (Brush?)GetValue(ValueBrushProperty);
        set => SetValue(ValueBrushProperty, value);
    }

    public Brush? ForecastBrush
    {
        get => (Brush?)GetValue(ForecastBrushProperty);
        set => SetValue(ForecastBrushProperty, value);
    }

    private static DependencyProperty Register<T>(string name, T defaultValue) =>
        DependencyProperty.Register(
            name,
            typeof(T),
            typeof(UsageGauge),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    protected override Size MeasureOverride(Size availableSize)
    {
        // Buen er 1,5 radius høj; uden højdebegrænsning følger højden bredden.
        var width = double.IsInfinity(availableSize.Width) ? 160 : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? width * 0.8 : availableSize.Height;
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var t = Thickness;
        var radius = Math.Min(ActualWidth - t, (ActualHeight - t) / 0.75) / 2;
        if (radius <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, t / 2 + radius);
        var value = Clamp(Value);

        DrawArc(dc, center, radius, 0, 100, TrackBrush, t);

        if (!double.IsNaN(Forecast) && ForecastBrush is { } forecastBrush)
        {
            var forecast = Clamp(Forecast);
            if (forecast > value)
            {
                var ghost = forecastBrush.CloneCurrentValue();
                ghost.Opacity = 0.28;
                DrawArc(dc, center, radius, value, forecast, ghost, t);
            }

            // Markør på tværs af buen ved prognosen.
            var angle = ToRadians(StartAngle + SweepAngle * forecast / 100);
            var direction = new Vector(Math.Cos(angle), Math.Sin(angle));
            var pen = new Pen(forecastBrush, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(pen, center + direction * (radius - t * 0.95), center + direction * (radius + t * 0.95));
        }

        if (value > 0)
        {
            DrawArc(dc, center, radius, 0, value, ValueBrush, t);
        }
    }

    private static void DrawArc(DrawingContext dc, Point center, double radius, double from, double to, Brush? brush, double thickness)
    {
        if (brush is null || to <= from)
        {
            return;
        }

        var startAngle = StartAngle + SweepAngle * from / 100;
        var sweep = SweepAngle * (to - from) / 100;
        var start = PointAt(center, radius, startAngle);
        var end = PointAt(center, radius, startAngle + sweep);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, isFilled: false, isClosed: false);
            context.ArcTo(end, new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, pen, geometry);
    }

    private static Point PointAt(Point center, double radius, double degrees)
    {
        var angle = ToRadians(degrees);
        return new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    private static double Clamp(double percent) => double.IsNaN(percent) ? 0 : Math.Clamp(percent, 0, 100);
}
