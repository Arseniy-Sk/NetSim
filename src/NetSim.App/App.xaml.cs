using System.Windows;
using System.Windows.Threading;
using NetSim.App.Localization;

namespace NetSim.App;

public partial class App : Application
{
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(Loc.F("Msg_Unhandled", e.Exception.Message), "NetSim", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
