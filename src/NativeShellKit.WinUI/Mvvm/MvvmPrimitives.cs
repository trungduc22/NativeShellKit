using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace NativeShellKit.WinUI.Mvvm;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public abstract class ViewModelBase : ObservableObject
{
    private bool _isBusy;
    private string? _statusMessage;

    public bool IsBusy
    {
        get => _isBusy;
        protected set => SetProperty(ref _isBusy, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        protected set => SetProperty(ref _statusMessage, value);
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(ConvertParam(parameter)) ?? true;

    public void Execute(object? parameter) => _execute(ConvertParam(parameter));

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private static T? ConvertParam(object? parameter) =>
        parameter is T typed ? typed : default;
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<CancellationToken, Task> _executeAsync;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;

    public AsyncRelayCommand(Func<CancellationToken, Task> executeAsync, Func<bool>? canExecute = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        await ExecuteAsync().ConfigureAwait(false);
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        if (!CanExecute(null))
            return;

        _isExecuting = true;
        NotifyCanExecuteChanged();
        try
        {
            await _executeAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _isExecuting = false;
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public static class ObservableCollectionSync
{
    /// <summary>
    /// Synchronizes an <see cref="ObservableCollection{TItem}"/> to match <paramref name="desired"/>
    /// by stable key without calling Clear(), preserving WinUI list virtualization and scroll position.
    /// </summary>
    public static void ApplyKeyedDiff<TItem, TKey>(
        this ObservableCollection<TItem> target,
        IReadOnlyList<TItem> desired,
        Func<TItem, TKey> keySelector,
        Func<TItem, TItem, bool>? contentEquals = null,
        Action<TItem, TItem>? updateInPlace = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(keySelector);

        var desiredKeys = new HashSet<TKey>();
        for (int i = 0; i < desired.Count; i++)
        {
            desiredKeys.Add(keySelector(desired[i]));
        }

        // Remove stale entries back-to-front
        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!desiredKeys.Contains(keySelector(target[i])))
            {
                target.RemoveAt(i);
            }
        }

        // Reconcile order and updates
        var eq = contentEquals ?? EqualityComparer<TItem>.Default.Equals;
        for (int targetIndex = 0; targetIndex < desired.Count; targetIndex++)
        {
            var nextItem = desired[targetIndex];
            var nextKey = keySelector(nextItem);

            int existingIndex = -1;
            for (int search = targetIndex; search < target.Count; search++)
            {
                if (EqualityComparer<TKey>.Default.Equals(keySelector(target[search]), nextKey))
                {
                    existingIndex = search;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                target.Insert(targetIndex, nextItem);
                continue;
            }

            if (existingIndex != targetIndex)
            {
                target.Move(existingIndex, targetIndex);
            }

            var currentItem = target[targetIndex];
            if (!eq(currentItem, nextItem))
            {
                if (updateInPlace is not null)
                    updateInPlace(currentItem, nextItem);
                else
                    target[targetIndex] = nextItem;
            }
        }

        while (target.Count > desired.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }
}
