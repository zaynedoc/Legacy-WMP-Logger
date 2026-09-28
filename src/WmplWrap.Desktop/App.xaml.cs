using System.Windows;

namespace WmplWrap.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        TaskbarIdentity.ConfigureProcess();
        base.OnStartup(e);
    }
}
