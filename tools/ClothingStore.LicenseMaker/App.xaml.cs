using System.Windows;
using System.Windows.Threading;

namespace ClothingStore.LicenseMaker;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnUnhandled;
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        MessageBox.Show(e.Exception.GetBaseException().Message, "License Maker", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
