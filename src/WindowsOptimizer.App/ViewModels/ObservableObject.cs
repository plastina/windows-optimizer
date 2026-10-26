using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace WindowsOptimizer.App.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise(params string?[] names)
    {
        foreach (var name in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>A command that runs an async action. Buttons bind IsEnabled to a view-model property instead of CanExecute.</summary>
public sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> _execute;

    public AsyncCommand(Func<Task> execute) => _execute = execute;

    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public async void Execute(object? parameter)
    {
        try
        {
            await _execute();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Windows Optimizer", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }
}
