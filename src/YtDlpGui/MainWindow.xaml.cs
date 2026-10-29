using System.ComponentModel;
using System.Windows;

namespace YtDlpGui;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();
    public MainWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
        Closing += OnClosing;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (ViewModel.IsBusy) ViewModel.Cancel();
        ViewModel.SaveSettings();
    }
}
