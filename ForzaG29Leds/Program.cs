namespace ForzaG29Leds;

public static class Program
{
    [System.STAThread]
    public static void Main()
    {
        // Extract WPF native DLLs and register their directory before any WPF
        // type is touched. No-op on self-contained builds (nothing embedded).
        NativeDllExtractor.ExtractAndRegister();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
