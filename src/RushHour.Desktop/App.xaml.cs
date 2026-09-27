using System.Windows;
namespace RushHour.Desktop;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RushHourGarage", "WebView2"));
        DispatcherUnhandledException += (_, args) =>
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RushHourGarage"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RushHourGarage", "error.log"), args.Exception.ToString());
            MessageBox.Show("起動できませんでした。Microsoft Edge WebView2 Runtime がインストールされているか確認してください。\n\n" + args.Exception.Message, "Rush Hour Garage");
            args.Handled = true; Shutdown(1);
        };
        base.OnStartup(e);
    }
}
