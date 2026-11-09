using System.Globalization;
using LoadLens.Core.Models;

namespace LoadLens.Core.Diagnostics;

/// <summary>
/// Rule-based diagnostic engine. Every rule must explain the cause, show the
/// evidence and suggest concrete actions instead of only reporting a number.
/// </summary>
public sealed class DiagnosisEngine
{
    private const int WarmupSamples = 5;
    private const int HistoryLimit = 30;

    private static readonly HashSet<string> ShellNoise = new(StringComparer.OrdinalIgnoreCase)
    {
        "LockApp", "ShellExperienceHost", "StartMenuExperienceHost", "ApplicationFrameHost",
        "SearchHost", "TextInputHost", "User OOBEBroker", "Widgets", "WidgetService"
    };

    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    private readonly int _logicalCores;
    private readonly List<SystemSnapshot> _history = new();

    public DiagnosisEngine(int logicalCores)
    {
        _logicalCores = Math.Max(1, logicalCores);
    }

    public void Reset() => _history.Clear();

    public DiagnosisResult Evaluate(SystemSnapshot snapshot)
    {
        _history.Add(snapshot);
        if (_history.Count > HistoryLimit)
        {
            _history.RemoveRange(0, _history.Count - HistoryLimit);
        }

        var target = SelectTarget(snapshot);
        var findings = new List<Finding>();
        var samples = _history.Count;

        AddMemoryFindings(findings, snapshot);
        AddVramFinding(findings, snapshot);
        AddGpuFinding(findings, snapshot, target);
        AddCpuFindings(findings, snapshot, target);
        AddDiskFinding(findings, snapshot);
        AddThermalFinding(findings, snapshot);
        AddDpcFinding(findings, snapshot);
        AddBackgroundFinding(findings, snapshot, target);

        var calibrating = samples < WarmupSamples;
        var ordered = findings
            .OrderByDescending(f => f.Severity)
            .ThenByDescending(f => f.Score)
            .ToList();

        Finding primary;
        if (calibrating && ordered.All(f => f.Severity < Severity.High))
        {
            primary = BuildCalibrationFinding(snapshot, samples);
            ordered.Clear();
        }
        else if (ordered.Count == 0)
        {
            primary = BuildHealthyFinding(snapshot);
        }
        else
        {
            primary = ordered[0];
            ordered.RemoveAt(0);
        }

        return new DiagnosisResult(
            primary,
            ordered.Take(4).ToArray(),
            target,
            calibrating,
            samples,
            snapshot.Timestamp);
    }

    private void AddMemoryFindings(List<Finding> findings, SystemSnapshot snapshot)
    {
        var memory = snapshot.Memory;
        if (memory.TotalMb <= 0)
        {
            return;
        }

        var recent = Last(3).Select(s => s.Memory).ToArray();
        var avgAvailable = recent.Average(m => m.AvailableMb);
        var avgCommit = recent.Average(m => m.CommitPercent);
        var avgPagesInput = recent.Average(m => m.PagesInputPerSecond);
        var threshold = Math.Max(900, memory.TotalMb * 0.05);
        var lowAvailable = memory.AvailableMb < threshold && avgAvailable < threshold * 1.2;
        var severe = memory.AvailableMb < 600 ||
                     (memory.AvailableMb < memory.TotalMb * 0.025 && avgCommit >= 90);
        var paging = avgPagesInput >= 120 || memory.PagesInputPerSecond >= 300;

        if (lowAvailable && (avgCommit >= 86 || paging || severe))
        {
            var severity = severe || avgCommit >= 96 || avgPagesInput >= 500
                ? Severity.Critical
                : Severity.High;

            findings.Add(new Finding(
                "ram-pressure",
                FindingKind.RamPressure,
                severity,
                "Системе не хватает оперативной памяти",
                $"Свободно {FormatMb(memory.AvailableMb)} из {FormatMb(memory.TotalMb)}. " +
                (paging
                    ? "Windows уже активно читает данные из файла подкачки — именно эти обращения к диску дают фризы и долгие отклики."
                    : "Память почти исчерпана, и системе приходится освобождать её выгрузкой на диск."),
                new[]
                {
                    new Evidence("Свободно RAM", FormatMb(memory.AvailableMb), $"{memory.UsedPercent:0}% занято"),
                    new Evidence("Commit", $"{FormatMb(memory.CommitUsedMb)} / {FormatMb(memory.CommitLimitMb)}", "выделенная память"),
                    new Evidence("Чтение из подкачки", $"{memory.PagesInputPerSecond:0} стр/с", "Pages Input/sec"),
                    new Evidence("Больше всего RAM", TopByRam(snapshot))
                },
                new[]
                {
                    "Закройте браузер с большим числом вкладок и фоновые приложения; начните с самых тяжёлых из списка процессов.",
                    "Проверьте, не растёт ли память одного процесса со временем — это может быть утечка. Перезапустите его.",
                    "Оставьте файл подкачки включённым и системного размера, желательно на SSD.",
                    "Если 32 ГБ не хватает постоянно, добавьте планку памяти: это даст больше, чем любая программная оптимизация."
                },
                100,
                0.9));
        }
        else if (avgCommit >= 92)
        {
            findings.Add(new Finding(
                "commit-pressure",
                FindingKind.CommitPressure,
                avgCommit >= 97 ? Severity.High : Severity.Medium,
                "Заканчивается выделяемая память",
                $"Commit charge {FormatMb(memory.CommitUsedMb)} из {FormatMb(memory.CommitLimitMb)}. " +
                "Физической RAM может быть достаточно, но система уже близка к пределу адресного пространства и файла подкачки. " +
                "Новые приложения будут запускаться медленно, возможны отказы в выделении памяти.",
                new[]
                {
                    new Evidence("Commit", $"{FormatMb(memory.CommitUsedMb)} / {FormatMb(memory.CommitLimitMb)}", $"{memory.CommitPercent:0}%"),
                    new Evidence("Свободно RAM", FormatMb(memory.AvailableMb)),
                    new Evidence("Файл подкачки", $"{memory.PagingFilePercent:0}%")
                },
                new[]
                {
                    "Закройте приложения, которые давно работают и держат много памяти.",
                    "Увеличьте файл подкачки на SSD до 8–16 ГБ или включите системное управление размером.",
                    "Проверьте процессы с наибольшим объёмом private memory на утечку."
                },
                74,
                0.72));
        }
    }

    private void AddVramFinding(List<Finding> findings, SystemSnapshot snapshot)
    {
        var gpu = snapshot.Gpu;
        if (!gpu.Available || gpu.VramUsagePercent is null || gpu.VramTotalMb is null)
        {
            return;
        }

        var recent = Last(3).Select(s => s.Gpu.VramUsagePercent).Where(v => v is not null).Select(v => v!.Value).ToArray();
        if (recent.Length < 2)
        {
            return;
        }

        var avgUsage = recent.Average();
        if (avgUsage < 88 || gpu.VramUsagePercent < 90)
        {
            return;
        }

        var shared = gpu.VramSharedMb ?? 0;
        var severity = avgUsage >= 97 || shared >= 1500
            ? Severity.Critical
            : avgUsage >= 94 || shared >= 750
                ? Severity.High
                : Severity.Medium;

        var targetVram = snapshot.Processes
            .OrderByDescending(p => p.VramMb)
            .FirstOrDefault();
        var evidence = new List<Evidence>
        {
            new("Занято VRAM", $"{FormatMb(gpu.VramUsedMb ?? 0)} / {FormatMb(gpu.VramTotalMb.Value)}", $"{gpu.VramUsagePercent:0}%"),
            new("Shared-память GPU", FormatMb(shared), "выгрузка в RAM"),
            new("Загрузка GPU", $"{gpu.LoadPercent:0}%", gpu.AdapterName)
        };
        if (targetVram is { VramMb: > 64 })
        {
            evidence.Add(new Evidence("Больше всего VRAM", $"{targetVram.DisplayName} · {FormatMb(targetVram.VramMb)}"));
        }

        findings.Add(new Finding(
            "vram-pressure",
            FindingKind.VramPressure,
            severity,
            "Видеопамять заполнена почти полностью",
            $"Занято {FormatMb(gpu.VramUsedMb ?? 0)} из {FormatMb(gpu.VramTotalMb.Value)} VRAM" +
            (shared > 128
                ? $", ещё {FormatMb(shared)} ушло в общую память. Часть текстур читается через PCIe, поэтому появляются микрофризы, подгрузка и провалы FPS."
                : ". Когда свободной VRAM не остаётся, драйвер начинает выгружать текстуры, что вызывает рывки."),
            evidence,
            new[]
            {
                "Снизьте качество текстур на одну ступень — это самый прямой способ освободить VRAM.",
                "Уменьшите разрешение или включите масштабирование (FSR/DLSS/XeSS), отключите трассировку лучей.",
                "Закройте браузер и другие приложения, использующие аппаратное ускорение GPU.",
                "После смены настроек перезапустите игру: некоторые движки не освобождают VRAM на лету."
            },
            severity == Severity.Critical ? 97 : 92,
            0.88));
    }

    private void AddGpuFinding(
        List<Finding> findings,
        SystemSnapshot snapshot,
        ProcessMetrics? target)
    {
        if (!snapshot.Gpu.Available)
        {
            return;
        }

        var recent = Last(3).ToArray();
        if (recent.Length < 3)
        {
            return;
        }

        var avgGpu = recent.Average(s => s.Gpu.LoadPercent);
        var avgCpu = recent.Average(s => s.Cpu.TotalPercent);
        var vram = recent
            .Select(s => s.Gpu.VramUsagePercent)
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .DefaultIfEmpty(0)
            .Average();

        if (avgGpu < 95 || avgCpu >= 88 || vram >= 90)
        {
            return;
        }

        var processText = target is null
            ? "Основной процесс"
            : $"{target.DisplayName} · GPU {target.GpuPercent:0}%";

        findings.Add(new Finding(
            "gpu-bound",
            FindingKind.GpuBound,
            Severity.Medium,
            "Видеокарта работает на пределе",
            $"GPU загружен на {avgGpu:0}%, при этом процессор загружен только на {avgCpu:0}%, а видеопамять не переполнена. " +
            "Похоже, FPS ограничен производительностью видеокарты — это не поломка и не утечка, а нехватка графической мощности для текущих настроек.",
            new[]
            {
                new Evidence("Загрузка GPU", $"{avgGpu:0}%", snapshot.Gpu.AdapterName),
                new Evidence("Загрузка CPU", $"{avgCpu:0}%", "процессор не является пределом"),
                new Evidence("VRAM", snapshot.Gpu.VramUsagePercent is { } v ? $"{v:0}%" : "н/д"),
                new Evidence("Процесс", processText)
            },
            new[]
            {
                "Снизьте качество графики: тени, отражения, эффекты и дальность прорисовки дают наибольший прирост.",
                "Включите FSR, DLSS или XeSS и/или уменьшите разрешение рендера.",
                "Ограничьте FPS чуть ниже среднего значения — это сделает кадры ровнее и снизит нагрев.",
                "Проверьте, что монитор подключён к дискретной видеокарте, а драйвер обновлён."
            },
            73,
            0.82));
    }

    private void AddCpuFindings(List<Finding> findings, SystemSnapshot snapshot, ProcessMetrics? target)
    {
        var recent = Last(3).ToArray();
        var cpuValues = recent.Select(s => s.Cpu.TotalPercent).ToArray();
        var avgCpu = cpuValues.Average();
        var queue = snapshot.Cpu.ProcessorQueueLength;

        var cpuSaturated = recent.Length >= 2 &&
                           (avgCpu >= 90 ||
                            (avgCpu >= 78 && queue > _logicalCores * 1.25));

        if (cpuSaturated)
        {
            var cpuSeverity = avgCpu >= 96 || queue > _logicalCores * 2
                ? Severity.High
                : Severity.Medium;

            findings.Add(new Finding(
                "cpu-saturated",
                FindingKind.CpuSaturated,
                cpuSeverity,
                "Все ядра процессора загружены",
                $"Средняя загрузка CPU {avgCpu:0}% за последние секунды. " +
                "Когда все логические ядра заняты, новые задачи ждут в очереди — отсюда лаги, подвисания интерфейса и просадки FPS.",
                new[]
                {
                    new Evidence("Загрузка CPU", $"{avgCpu:0}%", $"{_logicalCores} логических потоков"),
                    new Evidence("Очередь CPU", $"{queue:0.0}", "Processor Queue Length"),
                    new Evidence("Больше всего CPU", TopByCpu(snapshot, target))
                },
                new[]
                {
                    "Закройте программы из верхней части списка процессов; в первую очередь проверьте браузер, антивирус и лаунчеры.",
                    "Проверьте, не выполняются ли в фоне обновления Windows, индексация или сканирование антивируса.",
                    "Для ноутбука подключите питание и выберите схему «Производительность»; для ПК проверьте охлаждение.",
                    "Если CPU постоянно загружен без вашего участия, проверьте систему на майнер и автозапуск."
                },
                cpuSeverity == Severity.High ? 94 : 85,
                0.86));
        }

        if (target is null)
        {
            return;
        }

        var targetHistory = recent
            .Select(s => s.Processes.FirstOrDefault(p =>
                p.Pid == target.Pid ||
                string.Equals(p.Name, target.Name, StringComparison.OrdinalIgnoreCase)))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToArray();

        if (targetHistory.Length < 3)
        {
            return;
        }

        var lastThree = targetHistory.TakeLast(3).ToArray();
        var sustainedHotThread = lastThree.All(p => p.MaxThreadPercent >= 82);
        var avgGpu = recent.Average(s => s.Gpu.LoadPercent);
        var gpuNotTheLimit = !snapshot.Gpu.Available || avgGpu < 90;
        var minProcessCpu = Math.Max(0.6, 30.0 / _logicalCores);

        if (!sustainedHotThread ||
            target.CpuPercent < minProcessCpu ||
            avgCpu >= 88 ||
            !gpuNotTheLimit)
        {
            return;
        }

        var maxThread = lastThree.Average(p => p.MaxThreadPercent);
        var share = lastThree.Average(p => p.HottestThreadShare);

        var severity = maxThread >= 94 && avgGpu < 82 ? Severity.High : Severity.Medium;
        var name = target.DisplayName;
        var gpuText = snapshot.Gpu.Available ? $"{avgGpu:0}%" : "н/д";
        var evidence = new List<Evidence>
        {
            new("Самый горячий поток", $"{maxThread:0}% одного ядра"),
            new("Всего CPU", $"{avgCpu:0}%", $"{_logicalCores - 1}+ потоков свободны"),
            new("Загрузка GPU", gpuText, "не является пределом"),
            new("Потоков в процессе", target.ThreadCount.ToString(Ru))
        };
        if (share >= 0.5)
        {
            evidence.Add(new Evidence("Доля главного потока", $"{share * 100:0}%", "всей нагрузки процесса"));
        }

        findings.Add(new Finding(
            "single-thread",
            FindingKind.SingleThread,
            severity,
            $"Один поток {name} упирается в предел",
            $"В {name} один поток загружен на {maxThread:0}% одного ядра, хотя общая загрузка CPU всего {avgCpu:0}%. " +
            "Эта часть программы не распределяется по ядрам, поэтому FPS или отклик ограничены скоростью одного потока, " +
            "а не общей мощностью процессора.",
            evidence,
            new[]
            {
                "Ограничьте FPS, если это игра, или снизьте объём работы главного потока: ему станет легче.",
                "Проверьте в настройках игры/программы многопоточность, worker threads и физику — включите многопоточный режим, если он есть.",
                "Закройте фоновые программы, которые могут делить с игрой одно и то же ядро.",
                "Обновите игру и драйвер. Если движок принципиально однопоточный, это ограничение кода, а не неисправность ПК.",
                "Для такой задачи важнее однопоточная скорость CPU, чем число ядер: при апгрейде смотрите на IPC и частоту."
            },
            severity == Severity.High ? 90 : 79,
            severity == Severity.High ? 0.9 : 0.78,
            target));
    }

    private void AddDiskFinding(List<Finding> findings, SystemSnapshot snapshot)
    {
        var recent = Last(3).Select(s => s.Disk).ToArray();
        if (recent.Length < 3)
        {
            return;
        }

        var avgActive = recent.Average(d => d.ActivePercent);
        var avgQueue = recent.Average(d => d.QueueLength);
        var avgLatency = recent.Average(d => d.LatencyMs);
        var busy = avgActive >= 88 &&
                   (avgQueue >= 2 || avgLatency >= 25);
        var slow = avgLatency >= 45 && avgActive >= 60;

        if (!busy && !slow)
        {
            return;
        }

        var severity = avgActive >= 96 && (avgQueue >= 4 || avgLatency >= 60)
            ? Severity.High
            : Severity.Medium;

        findings.Add(new Finding(
            "disk-bound",
            FindingKind.DiskBound,
            severity,
            "Диск стал узким местом",
            $"Диск загружен на {avgActive:0}%, очередь запросов {avgQueue:0.0}, средняя задержка {avgLatency:0} мс. " +
            "Пока диск занят, игра и система ждут загрузки данных — это ощущается как фризы при подгрузке и долгие отклики.",
            new[]
            {
                new Evidence("Активность диска", $"{avgActive:0}%"),
                new Evidence("Очередь", $"{avgQueue:0.0}"),
                new Evidence("Задержка", $"{avgLatency:0} мс"),
                new Evidence("Обмен", $"{snapshot.Disk.ReadMbps + snapshot.Disk.WriteMbps:0.0} МБ/с")
            },
            new[]
            {
                "Откройте «Диспетчер задач → Производительность → Диск» и найдите процесс, который читает или пишет больше всех.",
                "Приостановите обновления Windows, индексацию и сканирование антивируса на время игры.",
                "Перенесите игру и файл подкачки на SSD; проверьте здоровье диска (SMART) и свободное место.",
                "Если диск загружен на 100% при низкой скорости, проверьте кабель SATA и очередь ошибок в журнале событий."
            },
            severity == Severity.High ? 84 : 76,
            0.8));
    }

    private void AddThermalFinding(List<Finding> findings, SystemSnapshot snapshot)
    {
        var recent = Last(3).ToArray();
        if (recent.Length < 3 || snapshot.Cpu.BaseFrequencyMhz <= 0)
        {
            return;
        }

        var avgPerf = recent.Average(s => s.Cpu.PerformancePercent);
        var avgCpu = recent.Average(s => s.Cpu.TotalPercent);
        if (avgCpu < 45 || avgPerf >= 76)
        {
            return;
        }

        var severity = avgPerf < 60 ? Severity.High : Severity.Medium;
        findings.Add(new Finding(
            "thermal",
            FindingKind.ThermalThrottle,
            severity,
            "Процессор, похоже, снижает частоту",
            $"Под нагрузкой {avgCpu:0}% процессор работает примерно на {avgPerf:0}% своей расчётной частоты. " +
            "Это похоже на троттлинг из-за температуры или ограничения питания: CPU успевает остыть, но теряет производительность.",
            new[]
            {
                new Evidence("Эффективная частота", $"{snapshot.Cpu.EffectiveFrequencyMhz / 1000:0.0} ГГц", $"база {snapshot.Cpu.BaseFrequencyMhz / 1000:0.0} ГГц"),
                new Evidence("От расчётной", $"{avgPerf:0}%"),
                new Evidence("Загрузка CPU", $"{avgCpu:0}%")
            },
            new[]
            {
                "Проверьте температуру CPU в HWiNFO или BIOS. Если под нагрузкой больше 90 °C — очистите охлаждение и замените термопасту.",
                "Убедитесь, что кулер и корпусные вентиляторы работают, а радиатор не забит пылью.",
                "Для ноутбука используйте подставку с охлаждением и режим максимальной производительности.",
                "Если температура в норме, проверьте лимиты питания в BIOS/фирменной утилите и схему электропитания Windows."
            },
            severity == Severity.High ? 86 : 78,
            0.72));
    }

    private void AddDpcFinding(List<Finding> findings, SystemSnapshot snapshot)
    {
        var recent = Last(3).Select(s => s.Cpu.DpcPercent + s.Cpu.InterruptPercent).ToArray();
        if (recent.Length < 3 || recent.Average() < 8)
        {
            return;
        }

        findings.Add(new Finding(
            "dpc-interrupt",
            FindingKind.DpcInterrupt,
            Severity.Medium,
            "Драйверы или устройства создают помехи",
            $"На обработку DPC и прерываний уходит в среднем {recent.Average():0.0}% CPU. " +
            "Это служебная работа драйверов; если она высока, система может дёргаться даже при свободных ядрах.",
            new[]
            {
                new Evidence("DPC + прерывания", $"{recent.Average():0.0}%", "за последние секунды"),
                new Evidence("Загрузка CPU", $"{snapshot.Cpu.TotalPercent:0}%")
            },
            new[]
            {
                "Обновите драйверы видеокарты, чипсета и сетевого адаптера с сайта производителя.",
                "Отключите по одному USB-устройства и проверьте, уходит ли проблема.",
                "Запустите LatencyMon на 5–10 минут и посмотрите, какой драйвер даёт наибольшую задержку.",
                "Проверьте, не установлены ли «оптимизаторы» и старые драйверы виртуальных устройств."
            },
            70,
            0.65));
    }

    private void AddBackgroundFinding(
        List<Finding> findings,
        SystemSnapshot snapshot,
        ProcessMetrics? target)
    {
        var busy = snapshot.Processes
            .Where(p => target is null || p.Pid != target.Pid)
            .Where(p => !ShellNoise.Contains(p.Name))
            .OrderByDescending(p => p.CpuPercent)
            .FirstOrDefault();

        if (busy is null || busy.CpuPercent < 22)
        {
            return;
        }

        if (snapshot.Cpu.TotalPercent >= 88)
        {
            return;
        }

        findings.Add(new Finding(
            "background-load",
            FindingKind.BackgroundLoad,
            Severity.Medium,
            $"Фоновая программа отнимает CPU: {busy.DisplayName}",
            $"{busy.DisplayName} использует {busy.CpuPercent:0}% всего CPU, пока проблемная программа работает. " +
            "Фоновые задачи могут перетягивать на себя ядра и создавать очереди, особенно если у них высокий приоритет.",
            new[]
            {
                new Evidence("Процесс", busy.DisplayName, $"PID {busy.Pid}"),
                new Evidence("CPU процесса", $"{busy.CpuPercent:0}%"),
                new Evidence("Потоков", busy.ThreadCount.ToString(Ru)),
                new Evidence("RAM процесса", FormatMb(busy.WorkingSetMb))
            },
            new[]
            {
                $"Закройте {busy.DisplayName} или ограничьте его фоновую активность.",
                "Проверьте автозапуск: «Диспетчер задач → Автозагрузка».",
                "Если это антивирус или индексатор, настройте исключение для папки с игрой."
            },
            66,
            0.7));
    }

    private ProcessMetrics? SelectTarget(SystemSnapshot snapshot)
    {
        return snapshot.Processes
            .Where(p => p.ActivityScore >= 2.5)
            .Where(p => !ShellNoise.Contains(p.Name))
            .Where(p => !p.Name.Equals("explorer", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.ActivityScore)
            .FirstOrDefault();
    }

    private Finding BuildCalibrationFinding(SystemSnapshot snapshot, int samples)
    {
        return new Finding(
            "calibration",
            FindingKind.Calibrating,
            Severity.Info,
            "Собираю картину",
            "Первые секунды нужны, чтобы измерить нагрузку и увидеть, какие потоки и процессы реально работают. " +
            "Запустите проблемную игру или программу и оставьте её работать — вердикт появится автоматически.",
            new[]
            {
                new Evidence("Наблюдение", $"{samples} из {WarmupSamples} с"),
                new Evidence("CPU", $"{snapshot.Cpu.TotalPercent:0}%"),
                new Evidence("GPU", snapshot.Gpu.Available ? $"{snapshot.Gpu.LoadPercent:0}%" : "н/д"),
                new Evidence("RAM", $"{snapshot.Memory.UsedPercent:0}%")
            },
            new[]
            {
                "Подождите 4–5 секунд.",
                "Запустите приложение, в котором наблюдаются лаги.",
                "Не закрывайте LoadLens: он продолжает наблюдать в фоне."
            },
            1,
            0.5);
    }

    private Finding BuildHealthyFinding(SystemSnapshot snapshot)
    {
        var recent = Last(5).ToArray();
        var avgCpu = recent.Average(s => s.Cpu.TotalPercent);
        var avgGpu = recent.Average(s => s.Gpu.LoadPercent);
        var avgRam = recent.Average(s => s.Memory.UsedPercent);
        var vram = snapshot.Gpu.VramUsagePercent;

        return new Finding(
            "healthy",
            FindingKind.NoIssue,
            Severity.Good,
            "Явных узких мест не видно",
            "За последние секунды ни один ресурс не выглядит перегруженным: CPU, память, видеопамять и диск в норме. " +
            "Если лаги остались, причина может быть внутри приложения — тяжёлые шейдеры, сеть, оптимизация — или проявляться короткими всплесками, которые легко пропустить.",
            new[]
            {
                new Evidence("CPU", $"{avgCpu:0}%"),
                new Evidence("GPU", snapshot.Gpu.Available ? $"{avgGpu:0}%" : "н/д"),
                new Evidence("RAM", $"{avgRam:0}%"),
                new Evidence("VRAM", vram is null ? "н/д" : $"{vram:0}%"),
                new Evidence("Диск", $"{snapshot.Disk.ActivePercent:0}%")
            },
            new[]
            {
                "Запустите отчёт в момент лага: короткий всплеск будет виден в истории.",
                "Проверьте температуру и частоту CPU/GPU в HWiNFO во время игры.",
                "Отключите оверлеи (Discord, GeForce Experience, MSI Afterburner) и проверьте без них.",
                "Если проблема только в сети, смотрите пинг и потери пакетов, а не загрузку железа."
            },
            0,
            0.62);
    }

    private IReadOnlyList<SystemSnapshot> Last(int count) =>
        _history.TakeLast(Math.Min(count, _history.Count)).ToArray();

    private string TopByCpu(SystemSnapshot snapshot, ProcessMetrics? target)
    {
        var list = snapshot.Processes
            .Where(p => target is null || p.Pid != target.Pid)
            .Where(p => p.CpuPercent >= 0.5)
            .OrderByDescending(p => p.CpuPercent)
            .Take(3)
            .Select(p => $"{p.DisplayName} {p.CpuPercent:0}%")
            .ToArray();

        return list.Length > 0 ? string.Join(", ", list) : "нет явного лидера";
    }

    private static string TopByRam(SystemSnapshot snapshot)
    {
        var list = snapshot.Processes
            .OrderByDescending(p => p.WorkingSetMb)
            .Take(3)
            .Select(p => $"{p.DisplayName} {FormatMb(p.WorkingSetMb)}")
            .ToArray();

        return list.Length > 0 ? string.Join(", ", list) : "нет данных";
    }

    private static string FormatMb(double mb)
    {
        if (mb >= 1024)
        {
            return $"{mb / 1024:0.0} ГБ";
        }

        return $"{mb:0} МБ";
    }
}
