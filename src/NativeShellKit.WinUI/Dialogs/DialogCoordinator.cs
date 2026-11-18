using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeShellKit.Abstractions.Navigation;

namespace NativeShellKit.WinUI.Dialogs;

public sealed class DialogCoordinator : IDialogService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private XamlRoot? _xamlRoot;

    public void AttachXamlRoot(XamlRoot xamlRoot)
    {
        _xamlRoot = xamlRoot;
    }

    public async Task<DialogOutcome> ShowMessageAsync(DialogRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_xamlRoot is null)
            return DialogOutcome.Primary;

        await _gate.WaitAsync(ct).ConfigureAwait(true);
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = _xamlRoot,
                Title = request.Title,
                Content = request.Message,
                PrimaryButtonText = request.PrimaryButtonText,
                SecondaryButtonText = request.SecondaryButtonText ?? string.Empty,
                CloseButtonText = request.CloseButtonText ?? string.Empty,
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync().AsTask(ct).ConfigureAwait(true);
            return result switch
            {
                ContentDialogResult.Primary => DialogOutcome.Primary,
                ContentDialogResult.Secondary => DialogOutcome.Secondary,
                _ => DialogOutcome.Closed
            };
        }
        finally
        {
            _gate.Release();
        }
    }
}
