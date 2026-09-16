using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BlockTheSpot.App;

public partial class App : Application
{
    private Mutex? instance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var smokeTest = e.Args.Length == 2 && e.Args[0] == "--smoke-test";
        if (smokeTest) Directory.CreateDirectory(e.Args[1]);
        DispatcherUnhandledException += (_, args) =>
        {
            var path = smokeTest ? Path.Combine(e.Args[1], "error.txt") : Path.Combine(Path.GetTempPath(), "BlockTheSpot-startup-error.log");
            try { File.WriteAllText(path, args.Exception.ToString()); } catch (IOException) { }
            if (!smokeTest) MessageBox.Show($"BlockTheSpot encountered an error.\n\n{args.Exception.Message}\n\nLog: {path}", "BlockTheSpot");
            args.Handled = true;
            Shutdown(1);
        };
        if (!smokeTest)
        {
            instance = new Mutex(true, @"Local\BlockTheSpotInstaller", out var created);
            if (!created) { MessageBox.Show("BlockTheSpot is already open.", "BlockTheSpot"); Shutdown(); return; }
        }
        try
        {
            var window = new MainWindow(smokeTest);
            MainWindow = window;
            window.Show();
            if (!smokeTest) return;
            foreach (var key in new[] { "CardBackgroundFillColorDefaultBrush", "TextFillColorPrimaryBrush", "AccentFillColorDefaultBrush" })
                if (window.TryFindResource(key) is null) throw new InvalidOperationException("Missing Fluent resource: " + key);
            if (window.VersionPicker.Items.Count != 1 || !window.Model.InstallCommand.CanExecute(null))
                throw new InvalidOperationException("Initial UI bindings did not initialize.");
            foreach (var dark in new[] { false, true })
            {
                ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                await Task.Delay(350);
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(e.Args[1], dark ? "windows-dark.png" : "windows-light.png"));
                encoder.Save(stream);
            }
            File.WriteAllText(Path.Combine(e.Args[1], "result.txt"), "Fluent resources, initial bindings, light theme and dark theme rendered successfully.");
            Shutdown(0);
        }
        catch (Exception error)
        {
            if (!smokeTest) throw;
            File.WriteAllText(Path.Combine(e.Args[1], "error.txt"), error.ToString());
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
