using System.Windows;
using Microsoft.Extensions.DependencyInjection;
namespace RushHour.Desktop;
public partial class MainWindow : Window
{
    private readonly ServiceProvider services;
    public MainWindow()
    {
        InitializeComponent();
        var collection = new ServiceCollection();
        collection.AddWpfBlazorWebView();
        services = collection.BuildServiceProvider();
        WebView.Services = services;
        Closed += async (_, _) => { await WebView.DisposeAsync(); await services.DisposeAsync(); };
    }
}
