using System.Globalization;
using System.Text;
using LoadLens.Core.Models;

namespace LoadLens.Core.Reports;

/// <summary>CSV export for Excel and ad-hoc analysis (semicolon + Russian decimal comma).</summary>
public static class CsvHistoryBuilder
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static string Build(IReadOnlyList<HistoryPoint> history)
    {
        var sb = new StringBuilder("Время;CPU %;GPU %;RAM %;VRAM %;Диск %\r\n");
        foreach (var point in history)
        {
            sb.Append(point.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", Ru)).Append(';')
                .Append(point.CpuPercent.ToString("0.0", Ru)).Append(';')
                .Append(point.GpuPercent.ToString("0.0", Ru)).Append(';')
                .Append(point.RamPercent.ToString("0.0", Ru)).Append(';')
                .Append(point.VramPercent.ToString("0.0", Ru)).Append(';')
                .Append(point.DiskPercent.ToString("0.0", Ru)).Append("\r\n");
        }

        return sb.ToString();
    }
}
