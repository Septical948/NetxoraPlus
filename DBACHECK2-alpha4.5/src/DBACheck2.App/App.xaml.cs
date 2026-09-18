using System.IO;
using System.Windows;

namespace DBACheck2.App;

public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Netxora", "DBACHECK2");
            Directory.CreateDirectory(dir);
            var log = Path.Combine(dir, "startup-error.log");
            File.WriteAllText(log, $"{DateTime.Now:O}\r\n{ex}");
            MessageBox.Show($"DBACHECK2 no pudo abrir la interfaz.\n\n{ex.Message}\n\nDetalle: {log}", "DBACHECK2 - Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
