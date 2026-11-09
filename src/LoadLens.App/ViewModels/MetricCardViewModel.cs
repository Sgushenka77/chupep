using System.Windows.Media;

namespace LoadLens.App.ViewModels;

public sealed class MetricCardViewModel : ObservableObject
{
    private string _value = "—";
    private string _caption = string.Empty;
    private string _status = "нет данных";
    private double _percent;
    private Brush _statusBrush;
    private IReadOnlyList<double> _spark = Array.Empty<double>();

    public MetricCardViewModel(string label, Brush accent, Brush idleBrush)
    {
        Label = label;
        Accent = accent;
        _statusBrush = idleBrush;
    }

    public string Label { get; }

    public Brush Accent { get; }

    public string Value
    {
        get => _value;
        private set => Set(ref _value, value);
    }

    public string Caption
    {
        get => _caption;
        private set => Set(ref _caption, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public double Percent
    {
        get => _percent;
        private set => Set(ref _percent, value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => Set(ref _statusBrush, value);
    }

    public IReadOnlyList<double> Spark
    {
        get => _spark;
        private set => Set(ref _spark, value);
    }

    public void Update(
        string value,
        string caption,
        double percent,
        string status,
        Brush statusBrush,
        IReadOnlyList<double> spark)
    {
        Value = value;
        Caption = caption;
        Percent = Math.Clamp(percent, 0, 100);
        Status = status;
        StatusBrush = statusBrush;
        Spark = spark;
    }
}
