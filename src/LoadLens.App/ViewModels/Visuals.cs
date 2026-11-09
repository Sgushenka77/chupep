using System.Windows;
using System.Windows.Media;
using LoadLens.Core.Models;

namespace LoadLens.App.ViewModels;

internal static class Visuals
{
    public static readonly Color Blue = Color.FromRgb(0x5B, 0x8C, 0xFF);
    public static readonly Color Purple = Color.FromRgb(0xA7, 0x8B, 0xFA);
    public static readonly Color Green = Color.FromRgb(0x35, 0xD0, 0x7F);
    public static readonly Color Amber = Color.FromRgb(0xF5, 0xB9, 0x42);
    public static readonly Color Orange = Color.FromRgb(0xFF, 0x8A, 0x4C);
    public static readonly Color Red = Color.FromRgb(0xFF, 0x5C, 0x6C);
    public static readonly Color Cyan = Color.FromRgb(0x38, 0xBD, 0xF8);
    public static readonly Color Pink = Color.FromRgb(0xFB, 0x71, 0x85);
    public static readonly Color Teal = Color.FromRgb(0x2D, 0xD4, 0xBF);

    private static readonly Color[] ProcessPalette =
    {
        Blue, Purple, Green, Amber, Orange, Cyan, Pink, Teal
    };

    private static readonly Dictionary<string, Brush> BrushCache = new(StringComparer.OrdinalIgnoreCase);

    public static Brush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static Brush Brush(string hex)
    {
        if (BrushCache.TryGetValue(hex, out var cached))
        {
            return cached;
        }

        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = Brush(color);
        BrushCache[hex] = brush;
        return brush;
    }

    public static Brush ProcessBrush(string name)
    {
        var hash = Fnv1A(name);
        return Brush(ProcessPalette[(int)(hash % (uint)ProcessPalette.Length)]);
    }

    public static string Initials(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }

        var cleaned = name.Trim();
        if (cleaned.Length == 1)
        {
            return cleaned.ToUpperInvariant();
        }

        return string.Concat(char.ToUpperInvariant(cleaned[0]), char.ToUpperInvariant(cleaned[1]));
    }

    public static Brush SeverityBrush(Severity severity) => severity switch
    {
        Severity.Critical => Brush(Red),
        Severity.High => Brush(Orange),
        Severity.Medium => Brush(Amber),
        Severity.Info => Brush(Blue),
        _ => Brush(Green)
    };

    public static Brush SeverityTint(Severity severity) => severity switch
    {
        Severity.Critical => Brush("#2AFF5C6C"),
        Severity.High => Brush("#2AFF8A4C"),
        Severity.Medium => Brush("#26F5B942"),
        Severity.Info => Brush("#245B8CFF"),
        _ => Brush("#2635D07F")
    };

    public static Brush HeroGradient(Severity severity)
    {
        var (from, to) = severity switch
        {
            Severity.Critical => (Color.FromRgb(0x2C, 0x12, 0x18), Color.FromRgb(0x14, 0x19, 0x22)),
            Severity.High => (Color.FromRgb(0x2B, 0x1B, 0x12), Color.FromRgb(0x14, 0x19, 0x22)),
            Severity.Medium => (Color.FromRgb(0x26, 0x21, 0x12), Color.FromRgb(0x14, 0x19, 0x22)),
            Severity.Info => (Color.FromRgb(0x11, 0x1D, 0x2D), Color.FromRgb(0x11, 0x18, 0x21)),
            _ => (Color.FromRgb(0x10, 0x22, 0x1B), Color.FromRgb(0x11, 0x18, 0x21))
        };

        var brush = new LinearGradientBrush(from, to, new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }

    public static Brush WithAlpha(Color color, byte alpha) => Brush(Color.FromArgb(alpha, color.R, color.G, color.B));

    public static string FormatMb(double mb)
    {
        if (mb >= 1024)
        {
            return $"{mb / 1024:0.0} ГБ";
        }

        return $"{mb:0} МБ";
    }

    public static string FormatGb(double mb) => $"{mb / 1024:0.0} ГБ";

    public static string Pct(double value) => $"{value:0}%";

    private static uint Fnv1A(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= 16777619u;
        }

        return hash;
    }
}
