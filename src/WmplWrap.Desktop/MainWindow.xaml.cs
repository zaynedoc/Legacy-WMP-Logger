using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace WmplWrap.Desktop;

public partial class MainWindow : Window
{
    private Forms.NotifyIcon? _trayIcon;
    private DashboardViewModel? _viewModel;
    private bool _exitRequested;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new DashboardViewModel();
        _viewModel.DiscordSettingsRequested += ScrollToDiscordSettings;
        DataContext = _viewModel;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exitRequested && _viewModel?.ShouldKeepRunningInBackground == true)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.DiscordSettingsRequested -= ScrollToDiscordSettings;
            _viewModel.Dispose();
            _viewModel = null;
        }
        DisposeTrayIcon();
        base.OnClosed(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TaskbarIdentity.ConfigureWindow(new WindowInteropHelper(this).Handle);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        if (e.Handled || _viewModel is null) return;

        if (e.ChangedButton == MouseButton.XButton1 && _viewModel.CanGoBack)
        {
            _viewModel.BackCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.XButton2 && _viewModel.CanGoForward)
        {
            _viewModel.ForwardCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OpenInWmpDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item?.DataContext is not { } target || DataContext is not DashboardViewModel viewModel) return;

        viewModel.OpenInWmp(target);
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void ScrollToDiscordSettings()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            SettingsScrollViewer.UpdateLayout();
            DiscordRpcSettingsCard.BringIntoView();
        }));
    }

    private void HideToTray()
    {
        EnsureTrayIcon();
        ShowInTaskbar = false;
        Hide();
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = true;
            return;
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open WMPL Wrap", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit WMPL Wrap", null, (_, _) => ExitFromTray());

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "WMPL Wrap — Discord Rich Presence",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.MouseDoubleClick += (_, _) => RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }));
    }

    private void ExitFromTray()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _exitRequested = true;
            Close();
        }));
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null) return;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private static Icon CreateTrayIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/wmpl_recap_icon_header.ico"));
            if (resource?.Stream is { } stream)
            {
                using (stream)
                using (var icon = new Icon(stream))
                    return (Icon)icon.Clone();
            }
        }
        catch { }

        return (Icon)SystemIcons.Application.Clone();
    }

}
