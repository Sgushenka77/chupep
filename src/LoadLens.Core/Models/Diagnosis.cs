namespace LoadLens.Core.Models;

public enum Severity
{
    Good = 0,
    Info = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum FindingKind
{
    Calibrating,
    NoIssue,
    SingleThread,
    CpuSaturated,
    RamPressure,
    CommitPressure,
    VramPressure,
    GpuBound,
    DiskBound,
    ThermalThrottle,
    DpcInterrupt,
    BackgroundLoad
}

/// <summary>A short, human-readable proof point behind a diagnosis.</summary>
public sealed record Evidence(string Label, string Value, string? Detail = null);

/// <summary>One explained cause of slowdowns.</summary>
public sealed record Finding(
    string Id,
    FindingKind Kind,
    Severity Severity,
    string Title,
    string Summary,
    IReadOnlyList<Evidence> Evidence,
    IReadOnlyList<string> Actions,
    double Score,
    double Confidence,
    ProcessMetrics? Target = null)
{
    public bool IsProblem => Severity >= Severity.Medium;

    public string SeverityLabel => Severity switch
    {
        Severity.Critical => "Критично",
        Severity.High => "Высокая вероятность",
        Severity.Medium => "Стоит проверить",
        Severity.Info => "Наблюдение",
        _ => "Норма"
    };
}

/// <summary>The result of the rule engine for the current moment.</summary>
public sealed record DiagnosisResult(
    Finding Primary,
    IReadOnlyList<Finding> Secondary,
    ProcessMetrics? Target,
    bool IsCalibrating,
    int SamplesObserved,
    DateTimeOffset Timestamp)
{
    public IEnumerable<Finding> All => new[] { Primary }.Concat(Secondary);

    public bool HasProblems => Primary.IsProblem || Secondary.Any(f => f.IsProblem);

    public Severity OverallSeverity => All
        .Select(f => f.Severity)
        .DefaultIfEmpty(Severity.Good)
        .Max();
}
