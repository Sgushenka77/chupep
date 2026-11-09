using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using LoadLens.App.ViewModels;

namespace LoadLens.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        // CenterScreen can place the window on a disconnected or secondary
        // virtual display. Always open on the primary work area.
        WindowStartupLocation = WindowStartupLocation.Manual;
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);

        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        SourceInitialized += (_, _) => ApplyDarkTitleBar();
        Loaded += (_, _) => Activate();
        Closed += (_, _) =>
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Dispose();
        };
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true } radio &&
            !string.IsNullOrWhiteSpace(radio.Uid) &&
            DataContext is MainViewModel vm)
        {
            vm.SelectedPage = radio.Uid;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.SelectedPage))
        {
            return;
        }

        var target = _viewModel.SelectedPage switch
        {
            "Processes" => NavProcesses,
            "Report" => NavReport,
            _ => NavOverview
        };

        if (target.IsChecked != true)
        {
            target.IsChecked = true;
        }
    }

    private void ApplyDarkTitleBar()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var enabled = 1;

            // DWMWA_USE_IMMERSIVE_DARK_MODE (20). Windows 10 builds before 20H1 use 19.
            if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
            }
        }
        catch
        {
            // A light title bar is cosmetic; never fail startup because of it.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
