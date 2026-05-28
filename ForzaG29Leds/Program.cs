namespace ForzaG29Leds;

public static class Program
{
    [System.STAThread]
    public static void Main()
    {
        // Prevent multiple instances — second copy shows a message and exits.
        using var mutex = new System.Threading.Mutex(true, "ForzaG29Leds_SingleInstance", out bool isNew);
        if (!isNew)
        {
            System.Windows.MessageBox.Show(
                "ForzaG29Leds is already running.\nCheck the system tray.",
                "Already Running",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        // Extract WPF native DLLs and register their directory before any WPF
        // type is touched. No-op on self-contained builds (nothing embedded).
        NativeDllExtractor.ExtractAndRegister();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
