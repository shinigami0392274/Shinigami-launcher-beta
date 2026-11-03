using System.Windows;
namespace FlagDeck;

public partial class App : Application
{
    Mutex? _mutex;

    public static void ApplyTheme(string name)
    {
        var d = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{name}.xaml") };
        var md = Current.Resources.MergedDictionaries;
        if (md.Count > 0) md[0] = d; else md.Add(d);
    }

    void OnStartup(object sender, StartupEventArgs e)
    {
        _mutex = new Mutex(true, "FlagDeck.SingleInstance", out bool created);
        if (!created) { Shutdown(); return; }
        Store.Load();
        ApplyTheme(Store.Data.Settings.Theme);
        var w = new MainWindow();
        MainWindow = w;
        if (!e.Args.Contains("--minimized")) w.Show();
    }
}
