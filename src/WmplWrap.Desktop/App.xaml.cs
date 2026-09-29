using System.Windows;

namespace WmplWrap.Desktop;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        TaskbarIdentity.ConfigureProcess();
        base.OnStartup(e);
    }
}
