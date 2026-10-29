using System.Windows.Input;

namespace YtDlpGui;

public sealed class RelayCommand(Action action, Func<bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => action();
    public event EventHandler? CanExecuteChanged
    { add => CommandRefresh.RequerySuggested += value; remove => CommandRefresh.RequerySuggested -= value; }
}

public sealed class AsyncCommand(Func<Task> action, Func<bool> canExecute, Action<Exception> onError) : ICommand
{
    private bool _running;
    public bool CanExecute(object? parameter) => !_running && canExecute();
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        CommandRefresh.InvalidateRequerySuggested();
        try { await action(); }
        catch (Exception ex) { onError(ex); }
        finally { _running = false; CommandRefresh.InvalidateRequerySuggested(); }
    }
    public event EventHandler? CanExecuteChanged
    { add => CommandRefresh.RequerySuggested += value; remove => CommandRefresh.RequerySuggested -= value; }
}

