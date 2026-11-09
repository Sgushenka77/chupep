using System.Globalization;
using System.Net;
using System.Text;
using LoadLens.Core.Models;

namespace LoadLens.Core.Reports;

/// <summary>Builds a self-contained HTML report (no external CSS or JS).</summary>
public static class HtmlReportBuilder
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static string Build(MonitoringUpdate update)
    {
        var snapshot = update.Snapshot;
        var diagnosis = update.Diagnosis;
        var primary = diagnosis.Primary;
        var info = snapshot.Info;
        var sb = new StringBuilder(32 * 1024);

        sb.Append("""
<!doctype html>
<html lang="ru">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>LoadLens — отчёт о диагностике</title>
<style>
:root { color-scheme: light; --ink:#15202b; --muted:#5d6b7a; --line:#dde5ec; --card:#ffffff; --bg:#f3f6f9; --blue:#356fe0; --green:#11845b; --amber:#b26a00; --red:#c6384b; }
* { box-sizing:border-box; }
body { margin:0; background:var(--bg); color:var(--ink); font:14px/1.55 "Segoe UI", system-ui, sans-serif; }
.wrap { max-width:1080px; margin:0 auto; padding:32px 24px 48px; }
.hero { background:linear-gradient(135deg,#101a2b,#1d3557); color:#fff; border-radius:18px; padding:28px 30px; }
.brand { display:flex; justify-content:space-between; gap:20px; align-items:center; font-size:12px; letter-spacing:.14em; text-transform:uppercase; color:#a9c1dc; }
.brand b { color:#fff; font-size:17px; letter-spacing:.08em; }
.meta { color:#9db5ce; font-size:12px; margin-top:4px; }
.badge { display:inline-block; border-radius:999px; padding:5px 10px; font-size:11px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; background:#233a54; color:#cfe0f3; }
h1 { margin:18px 0 8px; font-size:30px; line-height:1.2; }
.lead { margin:0; color:#d4e2f0; max-width:850px; font-size:15px; }
.target { margin-top:18px; padding:12px 14px; border:1px solid #38516d; background:#17263a; border-radius:10px; font-size:13px; color:#cfe0f3; }
.section { margin-top:22px; background:var(--card); border:1px solid var(--line); border-radius:14px; padding:22px 24px; }
h2 { margin:0 0 14px; font-size:17px; }
h3 { margin:0 0 4px; font-size:15px; }
.summary { color:var(--muted); margin:0 0 18px; }
.evidence { display:grid; grid-template-columns:repeat(auto-fit,minmax(190px,1fr)); gap:10px; margin:16px 0 0; }
.evidence div { background:#f7f9fb; border:1px solid var(--line); border-radius:10px; padding:10px 12px; }
.evidence span { display:block; color:var(--muted); font-size:11px; text-transform:uppercase; letter-spacing:.06em; }
.evidence b { font-size:15px; }
.evidence small { display:block; color:var(--muted); margin-top:2px; font-size:11px; }
ol.actions { margin:12px 0 0; padding-left:22px; }
ol.actions li { margin:6px 0; }
.finding { border-left:4px solid var(--line); padding:12px 16px; margin:12px 0; background:#f9fbfc; border-radius:0 10px 10px 0; }
.finding.high, .finding.critical { border-left-color:var(--red); }
.finding.medium { border-left-color:var(--amber); }
.finding.good { border-left-color:var(--green); }
.finding.info { border-left-color:var(--blue); }
.finding p { margin:4px 0; color:var(--muted); }
table { width:100%; border-collapse:collapse; font-size:13px; }
th { text-align:left; color:var(--muted); font-size:11px; text-transform:uppercase; letter-spacing:.05em; border-bottom:1px solid var(--line); padding:8px 6px; }
td { border-bottom:1px solid #edf1f5; padding:8px 6px; }
td.num, th.num { text-align:right; font-variant-numeric:tabular-nums; }
.metrics { display:grid; grid-template-columns:repeat(auto-fit,minmax(150px,1fr)); gap:10px; }
.metric { border:1px solid var(--line); border-radius:10px; padding:10px 12px; }
.metric span { color:var(--muted); font-size:11px; display:block; }
.metric b { font-size:18px; display:block; margin-top:2px; }
.metric small.note { display:block; margin-top:2px; line-height:1.35; }
.note { color:var(--muted); font-size:12px; }
.footer { text-align:center; color:var(--muted); font-size:11px; margin-top:22px; }
@media print { body { background:#fff; } .wrap { padding:0; } .hero { print-color-adjust:exact; -webkit-print-color-adjust:exact; } .section { break-inside:avoid; } }
</style>
</head>
<body>
<div class="wrap">
""");

        var severityClass = SeverityClass(primary.Severity);
        sb.Append("<section class=\"hero\">");
        sb.Append("<div class=\"brand\"><b>LOADLENS</b><span>диагностика нагрузки</span></div>");
        sb.Append("<div class=\"meta\">");
        sb.Append(WebUtility.HtmlEncode(snapshot.Timestamp.ToString("dd.MM.yyyy HH:mm:ss", Ru)));
        sb.Append(" · ");
        sb.Append(WebUtility.HtmlEncode(info.MachineName));
        sb.Append(" · ");
        sb.Append(WebUtility.HtmlEncode(info.OsName));
        sb.Append("</div>");
        sb.Append("<div class=\"badge\" style=\"margin-top:18px\">");
        sb.Append(WebUtility.HtmlEncode(PrimaryBadge(primary.Severity)));
        sb.Append("</div>");
        sb.Append("<h1>");
        sb.Append(WebUtility.HtmlEncode(primary.Title));
        sb.Append("</h1>");
        sb.Append("<p class=\"lead\">");
        sb.Append(WebUtility.HtmlEncode(primary.Summary));
        sb.Append("</p>");

        if (diagnosis.Target is { } target)
        {
            sb.Append("<div class=\"target\"><b>В центре внимания:</b> ");
            sb.Append(WebUtility.HtmlEncode(target.DisplayName));
            sb.Append(" · PID ");
            sb.Append(target.Pid.ToString(Ru));
            sb.Append(" · CPU ");
            sb.Append(Pct(target.CpuPercent));
            sb.Append(" · самый горячий поток ");
            sb.Append(Pct(target.MaxThreadPercent));
            sb.Append(" · GPU ");
            sb.Append(Pct(target.GpuPercent));
            sb.Append(" · VRAM ");
            sb.Append(FormatMb(target.VramMb));
            sb.Append("</div>");
        }

        sb.Append("</section>");

        AppendEvidenceAndActions(sb, primary);

        if (diagnosis.Secondary.Count > 0)
        {
            sb.Append("<section class=\"section\"><h2>Что ещё замечено</h2>");
            foreach (var finding in diagnosis.Secondary)
            {
                AppendFinding(sb, finding);
            }

            sb.Append("</section>");
        }

        var metrics = new List<(string Label, string Value, string? Note)>
        {
            ("CPU", Pct(snapshot.Cpu.TotalPercent), $"{info.LogicalCores} логических потоков"),
            ("Самый горячий поток CPU", Pct(snapshot.Cpu.MaxCorePercent), "по логическим ядрам"),
            ("GPU", snapshot.Gpu.Available ? Pct(snapshot.Gpu.LoadPercent) : "н/д", snapshot.Gpu.AdapterName),
            ("VRAM", snapshot.Gpu.VramUsagePercent is { } vram
                ? $"{Pct(vram)} · {FormatMb(snapshot.Gpu.VramUsedMb ?? 0)} / {FormatMb(snapshot.Gpu.VramTotalMb ?? 0)}"
                : "н/д", "видеопамять"),
            ("RAM", $"{Pct(snapshot.Memory.UsedPercent)} · {FormatMb(snapshot.Memory.AvailableMb)} свободно",
                $"commit {Pct(snapshot.Memory.CommitPercent)}"),
            ("Диск", Pct(snapshot.Disk.ActivePercent), $"очередь {snapshot.Disk.QueueLength:0.0} · задержка {snapshot.Disk.LatencyMs:0} мс"),
            ("Частота CPU", $"{snapshot.Cpu.EffectiveFrequencyMhz / 1000:0.0} ГГц",
                $"база {snapshot.Cpu.BaseFrequencyMhz / 1000:0.0} ГГц · {Pct(snapshot.Cpu.PerformancePercent)} от расчётной"),
            ("DPC + прерывания", Pct(snapshot.Cpu.DpcPercent + snapshot.Cpu.InterruptPercent), "служебная нагрузка драйверов")
        };

        sb.Append("<section class=\"section\"><h2>Ключевые показатели</h2><div class=\"metrics\">");
        foreach (var (label, value, note) in metrics)
        {
            sb.Append("<div class=\"metric\"><span>");
            sb.Append(WebUtility.HtmlEncode(label));
            sb.Append("</span><b>");
            sb.Append(WebUtility.HtmlEncode(value));
            sb.Append("</b>");
            if (!string.IsNullOrWhiteSpace(note))
            {
                sb.Append("<small class=\"note\">");
                sb.Append(WebUtility.HtmlEncode(note));
                sb.Append("</small>");
            }

            sb.Append("</div>");
        }

        sb.Append("</div></section>");

        sb.Append("<section class=\"section\"><h2>Средние и пиковые значения за сессию</h2>");
        sb.Append("<table><thead><tr><th>Ресурс</th><th class=\"num\">Среднее</th><th class=\"num\">Пик</th></tr></thead><tbody>");
        AppendStatRow(sb, "CPU", update.Stats.AvgCpuPercent, update.Stats.PeakCpuPercent);
        AppendStatRow(sb, "GPU", update.Stats.AvgGpuPercent, update.Stats.PeakGpuPercent);
        AppendStatRow(sb, "RAM", update.Stats.AvgRamPercent, update.Stats.PeakRamPercent);
        AppendStatRow(sb, "VRAM", null, update.Stats.PeakVramPercent);
        sb.Append("</tbody></table>");
        sb.Append("<p class=\"note\">Длительность наблюдения: ");
        sb.Append(WebUtility.HtmlEncode(FormatDuration(update.Stats.Duration)));
        sb.Append(" · выборок: ");
        sb.Append(update.Stats.Samples.ToString(Ru));
        sb.Append(".</p></section>");

        sb.Append("<section class=\"section\"><h2>Активные процессы</h2>");
        sb.Append("<table><thead><tr><th>Процесс</th><th class=\"num\">CPU</th><th class=\"num\">1 поток</th><th class=\"num\">GPU</th><th class=\"num\">VRAM</th><th class=\"num\">RAM</th><th class=\"num\">Потоков</th></tr></thead><tbody>");
        foreach (var process in snapshot.Processes
                     .OrderByDescending(p => p.ActivityScore)
                     .Take(12))
        {
            sb.Append("<tr><td>");
            sb.Append(WebUtility.HtmlEncode(process.DisplayName));
            sb.Append("<br><span class=\"note\">PID ");
            sb.Append(process.Pid.ToString(Ru));
            sb.Append("</span></td><td class=\"num\">");
            sb.Append(Pct(process.CpuPercent));
            sb.Append("</td><td class=\"num\">");
            sb.Append(Pct(process.MaxThreadPercent));
            sb.Append("</td><td class=\"num\">");
            sb.Append(Pct(process.GpuPercent));
            sb.Append("</td><td class=\"num\">");
            sb.Append(FormatMb(process.VramMb));
            sb.Append("</td><td class=\"num\">");
            sb.Append(FormatMb(process.WorkingSetMb));
            sb.Append("</td><td class=\"num\">");
            sb.Append(process.ThreadCount.ToString(Ru));
            sb.Append("</td></tr>");
        }

        sb.Append("</tbody></table></section>");

        sb.Append("<section class=\"section\"><h2>Оборудование</h2><table><tbody>");
        AppendInfoRow(sb, "Процессор", $"{info.CpuName} · {info.PhysicalCores} ядер / {info.LogicalCores} потоков");
        AppendInfoRow(sb, "Видеокарта", $"{info.GpuName} · {FormatMb(info.GpuMemoryMb)}");
        AppendInfoRow(sb, "Оперативная память", FormatMb(info.RamTotalMb));
        AppendInfoRow(sb, "Система", info.OsName);
        AppendInfoRow(sb, "Права", info.IsElevated ? "администратор" : "обычный пользователь");
        AppendInfoRow(sb, "Версия LoadLens", info.AppVersion);
        sb.Append("</tbody></table></section>");

        sb.Append("<p class=\"footer\">Отчёт сформирован LoadLens. Диагностика основана на системных счётчиках Windows; ");
        sb.Append("приложение не измеряет frame time внутри игры. Для проверки коротких всплесков запускайте отчёт в момент лага.</p>");
        sb.Append("</div></body></html>");

        return sb.ToString();
    }

    private static void AppendEvidenceAndActions(StringBuilder sb, Finding finding)
    {
        sb.Append("<section class=\"section\"><h2>Доказательства</h2><div class=\"evidence\">");
        foreach (var evidence in finding.Evidence)
        {
            sb.Append("<div><span>");
            sb.Append(WebUtility.HtmlEncode(evidence.Label));
            sb.Append("</span><b>");
            sb.Append(WebUtility.HtmlEncode(evidence.Value));
            sb.Append("</b>");
            if (!string.IsNullOrWhiteSpace(evidence.Detail))
            {
                sb.Append("<small>");
                sb.Append(WebUtility.HtmlEncode(evidence.Detail));
                sb.Append("</small>");
            }

            sb.Append("</div>");
        }

        sb.Append("</div>");
        if (finding.Actions.Count > 0)
        {
            sb.Append("<h2 style=\"margin-top:22px\">Что сделать</h2><ol class=\"actions\">");
            foreach (var action in finding.Actions)
            {
                sb.Append("<li>");
                sb.Append(WebUtility.HtmlEncode(action));
                sb.Append("</li>");
            }

            sb.Append("</ol>");
        }

        sb.Append("</section>");
    }

    private static void AppendFinding(StringBuilder sb, Finding finding)
    {
        sb.Append("<div class=\"finding ");
        sb.Append(SeverityClass(finding.Severity));
        sb.Append("\"><h3>");
        sb.Append(WebUtility.HtmlEncode(finding.Title));
        sb.Append("</h3><p>");
        sb.Append(WebUtility.HtmlEncode(finding.Summary));
        sb.Append("</p></div>");
    }

    private static void AppendStatRow(StringBuilder sb, string label, double? average, double peak)
    {
        sb.Append("<tr><td>");
        sb.Append(WebUtility.HtmlEncode(label));
        sb.Append("</td><td class=\"num\">");
        sb.Append(average is null ? "—" : Pct(average.Value));
        sb.Append("</td><td class=\"num\">");
        sb.Append(Pct(peak));
        sb.Append("</td></tr>");
    }

    private static void AppendInfoRow(StringBuilder sb, string label, string value)
    {
        sb.Append("<tr><td style=\"width:210px\">");
        sb.Append(WebUtility.HtmlEncode(label));
        sb.Append("</td><td>");
        sb.Append(WebUtility.HtmlEncode(value));
        sb.Append("</td></tr>");
    }

    private static string Pct(double value) => $"{value:0}%";

    private static string FormatMb(double mb)
    {
        if (mb >= 1024)
        {
            return $"{mb / 1024:0.0} ГБ";
        }

        return $"{mb:0} МБ";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours} ч {duration.Minutes} мин";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes} мин {duration.Seconds} с";
        }

        return $"{Math.Max(0, duration.Seconds)} с";
    }

    private static string PrimaryBadge(Severity severity) => severity switch
    {
        Severity.Critical => "Критическая нехватка ресурса",
        Severity.High => "Высокая вероятность причины",
        Severity.Medium => "Вероятная причина",
        Severity.Info => "Наблюдение",
        _ => "Явных проблем не найдено"
    };

    private static string SeverityClass(Severity severity) => severity switch
    {
        Severity.Critical => "critical",
        Severity.High => "high",
        Severity.Medium => "medium",
        Severity.Info => "info",
        _ => "good"
    };
}
