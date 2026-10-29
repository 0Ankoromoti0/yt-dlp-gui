using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace YtDlpGui;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            window.Opened += async (_, _) =>
            {
                try { await window.ViewModel.InitializeAsync(); }
                catch (Exception ex) { window.ViewModel.ShowError(ex); }
                // Release CI exercises the real native window and exits after layout/tool checks.
                if (desktop.Args?.Contains("--smoke-test") == true)
                {
                    try
                    {
                        await window.SmokeTestAsync();
                        desktop.Shutdown(0);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex);
                        desktop.Shutdown(1);
                    }
                }
            };
            desktop.ShutdownRequested += (_, _) => { if (window.ViewModel.IsBusy) window.ViewModel.Cancel(); window.ViewModel.SaveSettings(); };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
