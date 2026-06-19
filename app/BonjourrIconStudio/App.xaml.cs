using System.Windows;

namespace BonjourrIconStudio;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"Произошла непредвиденная ошибка.\n\n{args.Exception.Message}",
                "Bonjourr Icon Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
