using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace HerdDesk.App.Controls;

public sealed partial class SearchPalette : UserControl
{
    private ShellViewModel? _shell;

    public SearchPalette()
    {
        InitializeComponent();
    }

    public event EventHandler? Activated;

    public void Bind(ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
        QueryBox.Text = shell.Search.Query;
        ResultList.ItemsSource = shell.Search.Results;
    }

    public void FocusQuery() => QueryBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);

    private void OnQueryChanged(object sender, TextChangedEventArgs args)
    {
        _ = (sender, args);
        if (_shell is null)
            return;
        _shell.Search.IsComposing = false;
        _shell.Search.SetQuery(QueryBox.Text ?? "");
        ResultList.ItemsSource = _shell.Search.Results;
    }

    private void OnQueryPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        _ = sender;
        if (_shell is null)
            return;
        if ((int)args.Key == 229)
        {
            _shell.Search.IsComposing = true;
            return;
        }

        if (args.Key == VirtualKey.Escape)
        {
            _shell.CloseSearch();
            args.Handled = true;
            return;
        }

        if (args.Key == VirtualKey.Enter)
        {
            if (_shell.Search.IsComposing)
                return;
            args.Handled = _shell.ActivateSearchResult();
            if (args.Handled)
                Activated?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (args.Key == VirtualKey.Down)
        {
            _shell.Search.MoveSelection(1);
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Up)
        {
            _shell.Search.MoveSelection(-1);
            args.Handled = true;
        }
    }

    private void OnResultClick(object sender, ItemClickEventArgs args)
    {
        _ = sender;
        if (_shell is null || args.ClickedItem is not SearchHit hit)
            return;
        var index = _shell.Search.Results.ToList().FindIndex(item =>
            item.Device == hit.Device &&
            item.Session == hit.Session &&
            item.WorkspaceId == hit.WorkspaceId &&
            item.Pane == hit.Pane);
        if (index < 0)
            return;
        _shell.Search.SelectIndex(index);
        if (_shell.ActivateSearchResult())
            Activated?.Invoke(this, EventArgs.Empty);
    }
}
