using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Abstractions.Threading;
using NativeShellKit.WinUI.Threading;
using Windows.UI;

namespace NativeShellKit.WinUI.Dialogs;

public sealed record FormDialogRequest<TResult>(
    string Title,
    Func<object> ContentFactory,
    string PrimaryButtonText = "Save",
    string CloseButtonText = "Cancel",
    Func<CancellationToken, Task<bool>>? ValidateAsync = null,
    Func<TResult>? ReadResult = null,
    DialogDefaultButton DefaultButton = DialogDefaultButton.Primary);

public sealed class DialogCoordinator : IDialogService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<Func<Task<DialogOutcome>>, CancellationToken, Task<DialogOutcome>>? _customRunner;
    private IUiDispatcher? _dispatcher;
    private XamlRoot? _xamlRoot;

    public DialogCoordinator(IUiDispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher;
    }

    internal DialogCoordinator(
        IUiDispatcher? dispatcher,
        Func<Func<Task<DialogOutcome>>, CancellationToken, Task<DialogOutcome>> customRunner)
    {
        _dispatcher = dispatcher;
        _customRunner = customRunner;
    }

    public void AttachXamlRoot(XamlRoot xamlRoot)
    {
        _xamlRoot = xamlRoot;
        if (_dispatcher is null && xamlRoot.DispatcherQueue is not null)
        {
            _dispatcher = new UiDispatcherHelper(xamlRoot.DispatcherQueue);
        }
    }

    public async Task<DialogOutcome> ShowMessageAsync(DialogRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await RunSerializedOnUiAsync(async () =>
        {
            if (_xamlRoot is null)
                return DialogOutcome.Primary;

            var dialog = new ContentDialog
            {
                XamlRoot = _xamlRoot,
                Title = request.Title,
                Content = request.Message,
                PrimaryButtonText = request.PrimaryButtonText,
                SecondaryButtonText = request.SecondaryButtonText ?? string.Empty,
                CloseButtonText = request.CloseButtonText ?? string.Empty,
                DefaultButton = MapDefaultButton(request.DefaultButton)
            };

            var result = await dialog.ShowAsync().AsTask(ct).ConfigureAwait(true);
            return MapOutcome(result);
        }, ct).ConfigureAwait(false);
    }

    public async Task<DialogOutcome> ShowDestructiveConfirmAsync(DestructiveDialogRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await RunSerializedOnUiAsync(async () =>
        {
            if (_xamlRoot is null)
                return DialogOutcome.Closed;

            var dangerStyle = new Style(typeof(Button));
            Brush? dangerFg = Application.Current?.Resources?.TryGetValue("NskStatusDangerFgBrush", out var brushObj) == true
                ? brushObj as Brush
                : null;
            dangerStyle.Setters.Add(new Setter(
                Control.ForegroundProperty,
                dangerFg ?? new SolidColorBrush(Color.FromArgb(255, 196, 43, 28))));

            var dialog = new ContentDialog
            {
                XamlRoot = _xamlRoot,
                Title = request.Title,
                Content = request.Message,
                PrimaryButtonText = request.ConfirmButtonText,
                PrimaryButtonStyle = dangerStyle,
                CloseButtonText = request.CancelButtonText,
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync().AsTask(ct).ConfigureAwait(true);
            return MapOutcome(result);
        }, ct).ConfigureAwait(false);
    }

    public async Task ShowProgressAsync(
        ProgressDialogRequest request,
        Func<IProgress<DialogProgressUpdate>, CancellationToken, Task> work,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(work);

        if (_xamlRoot is null && _customRunner is null)
        {
            await work(new Progress<DialogProgressUpdate>(), ct).ConfigureAwait(false);
            return;
        }

        await RunSerializedOnUiAsync(async () =>
        {
            if (_xamlRoot is null)
            {
                await work(new Progress<DialogProgressUpdate>(), ct).ConfigureAwait(true);
                return DialogOutcome.Primary;
            }

            using var workCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var statusBlock = new TextBlock
            {
                Text = request.InitialStatus,
                TextWrapping = TextWrapping.Wrap
            };
            var progressBar = new ProgressBar
            {
                IsIndeterminate = true,
                Minimum = 0,
                Maximum = 100
            };

            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(statusBlock);
            panel.Children.Add(progressBar);

            var dialog = new ContentDialog
            {
                XamlRoot = _xamlRoot,
                Title = request.Title,
                Content = panel,
                CloseButtonText = request.IsCancelable ? request.CancelButtonText : string.Empty,
                DefaultButton = request.IsCancelable ? ContentDialogButton.Close : ContentDialogButton.None
            };

            void OnCloseClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
            {
                workCts.Cancel();
            }

            if (request.IsCancelable)
                dialog.CloseButtonClick += OnCloseClicked;

            var progress = new Progress<DialogProgressUpdate>(update =>
            {
                void UpdateUi()
                {
                    statusBlock.Text = update.Status;
                    if (update.Percent.HasValue)
                    {
                        progressBar.IsIndeterminate = false;
                        progressBar.Value = Math.Clamp(update.Percent.Value, 0, 100);
                    }
                    else
                    {
                        progressBar.IsIndeterminate = true;
                    }
                }

                if (_dispatcher is not null)
                    _dispatcher.TryEnqueue(UpdateUi);
                else if (_xamlRoot.DispatcherQueue is { } q)
                    q.TryEnqueue(UpdateUi);
                else
                    UpdateUi();
            });

            var showTask = dialog.ShowAsync().AsTask();
            try
            {
                await work(progress, workCts.Token).ConfigureAwait(true);
            }
            finally
            {
                if (request.IsCancelable)
                    dialog.CloseButtonClick -= OnCloseClicked;

                try
                {
                    dialog.Hide();
                }
                catch
                {
                    // Dialog may already be closed if user canceled.
                }

                try
                {
                    await showTask.ConfigureAwait(true);
                }
                catch
                {
                    // Ignore closing errors after Hide().
                }
            }

            return DialogOutcome.Primary;
        }, ct).ConfigureAwait(false);
    }

    public async Task<(DialogOutcome Outcome, TResult? Result)> ShowFormAsync<TResult>(
        FormDialogRequest<TResult> request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        TResult? captured = default;
        var outcome = await RunSerializedOnUiAsync(async () =>
        {
            if (_xamlRoot is null)
                return DialogOutcome.Closed;

            var dialog = new ContentDialog
            {
                XamlRoot = _xamlRoot,
                Title = request.Title,
                Content = request.ContentFactory(),
                PrimaryButtonText = request.PrimaryButtonText,
                CloseButtonText = request.CloseButtonText,
                DefaultButton = MapDefaultButton(request.DefaultButton)
            };

            async void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
            {
                if (request.ValidateAsync is null)
                {
                    if (request.ReadResult is not null)
                        captured = request.ReadResult();
                    return;
                }

                var deferral = args.GetDeferral();
                try
                {
                    bool valid = await request.ValidateAsync(ct).ConfigureAwait(true);
                    if (!valid)
                    {
                        args.Cancel = true;
                    }
                    else if (request.ReadResult is not null)
                    {
                        captured = request.ReadResult();
                    }
                }
                finally
                {
                    deferral.Complete();
                }
            }

            dialog.PrimaryButtonClick += OnPrimaryClick;
            try
            {
                var result = await dialog.ShowAsync().AsTask(ct).ConfigureAwait(true);
                return MapOutcome(result);
            }
            finally
            {
                dialog.PrimaryButtonClick -= OnPrimaryClick;
            }
        }, ct).ConfigureAwait(false);

        return (outcome, outcome == DialogOutcome.Primary ? captured : default);
    }

    private async Task<DialogOutcome> RunSerializedOnUiAsync(
        Func<Task<DialogOutcome>> actionOnUi,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Func<Task<DialogOutcome>> runner = _customRunner is not null
                ? () => _customRunner(actionOnUi, ct)
                : actionOnUi;

            if (_dispatcher is not null && !_dispatcher.HasThreadAccess)
            {
                DialogOutcome result = DialogOutcome.Closed;
                await _dispatcher.EnqueueAsync(async () =>
                {
                    result = await runner().ConfigureAwait(true);
                }, ct).ConfigureAwait(false);
                return result;
            }

            if (_xamlRoot?.DispatcherQueue is { HasThreadAccess: false } queue)
            {
                DialogOutcome result = DialogOutcome.Closed;
                var helper = new UiDispatcherHelper(queue);
                await helper.EnqueueAsync(async () =>
                {
                    result = await runner().ConfigureAwait(true);
                }, ct).ConfigureAwait(false);
                return result;
            }

            return await runner().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static ContentDialogButton MapDefaultButton(DialogDefaultButton button) => button switch
    {
        DialogDefaultButton.Secondary => ContentDialogButton.Secondary,
        DialogDefaultButton.Close => ContentDialogButton.Close,
        _ => ContentDialogButton.Primary
    };

    private static DialogOutcome MapOutcome(ContentDialogResult result) => result switch
    {
        ContentDialogResult.Primary => DialogOutcome.Primary,
        ContentDialogResult.Secondary => DialogOutcome.Secondary,
        _ => DialogOutcome.Closed
    };
}
