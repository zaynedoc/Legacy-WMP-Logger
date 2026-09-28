using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace WmplWrap.Desktop;

public partial class MainWindow : Window
{
    private const uint WmSetIcon = 0x0080;
    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x0010;
    private static readonly IntPtr IconBig = new(1);
    private IntPtr _taskbarIcon;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new DashboardViewModel();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "wmpl_recap_icon_bar.ico");
        if (!File.Exists(iconPath)) return;
        _taskbarIcon = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LoadFromFile);
        if (_taskbarIcon != IntPtr.Zero)
            SendMessage(new WindowInteropHelper(this).Handle, WmSetIcon, IconBig, _taskbarIcon);
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

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr instance, string path, uint imageType, int width, int height, uint loadFlags);
}
