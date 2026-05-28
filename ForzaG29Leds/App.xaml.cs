using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using Application = System.Windows.Application;

namespace ForzaG29Leds;

public partial class App : Application
{
    private NotifyIcon?       _tray;
    private TelemetryService? _service;
    private Settings          _settings = Settings.Load();
    private SettingsWindow?   _settingsWindow;

    private bool _wheelConnected;
    private bool _telemetryActive;

    // Icons created once on the UI thread at startup and reused.
    private Icon _idleIcon  = null!;
    private Icon _readyIcon = null!;
    private Icon _liveIcon  = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _idleIcon  = BuildIcon(Color.FromArgb(120, 120, 120));
        _readyIcon = BuildIcon(Color.FromArgb(80,  200, 80));
        _liveIcon  = BuildIcon(Color.FromArgb(255, 220, 60));

        _service = new TelemetryService();
        _service.WheelStatusChanged     += OnWheelStatus;
        _service.TelemetryStatusChanged += OnTelemetryStatus;
        _service.ServiceError           += OnServiceError;
        _service.Start(_settings);

        _tray = new NotifyIcon
        {
            Icon             = _idleIcon,
            Visible          = true,
            Text             = "ForzaG29Leds — starting…",
            ContextMenuStrip = BuildMenu(),
        };
        _tray.DoubleClick += (_, _) => OpenSettings();
    }

    // ── Tray icon state ───────────────────────────────────────────────────────

    private void OnWheelStatus(bool connected)
    {
        _wheelConnected = connected;
        Dispatcher.Invoke(RefreshIcon);
    }

    private void OnTelemetryStatus(bool receiving)
    {
        _telemetryActive = receiving;
        Dispatcher.Invoke(RefreshIcon);
    }

    private void OnServiceError(string message) =>
        Dispatcher.Invoke(() =>
            _tray?.ShowBalloonTip(6000, "ForzaG29Leds", message, ToolTipIcon.Warning));

    private void RefreshIcon()
    {
        if (_tray is null) return;

        Icon   icon;
        string tip;

        if (!_wheelConnected)
        {
            icon = _idleIcon;
            tip  = "ForzaG29Leds — Wheel not found";
        }
        else if (_telemetryActive)
        {
            icon = _liveIcon;
            tip  = "ForzaG29Leds — Live";
        }
        else
        {
            icon = _readyIcon;
            tip  = "ForzaG29Leds — Wheel ready";
        }

        _tray.Icon = icon;
        _tray.Text = tip;
    }

    private static Icon BuildIcon(Color fill)
    {
        using var bmp = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(fill);
            g.FillEllipse(brush, 2, 2, 12, 12);
        }
        IntPtr hIcon = bmp.GetHicon();
        // Use 'using' so the wrapper is disposed before DestroyIcon — prevents
        // the finalizer calling DestroyIcon on an already-freed handle.
        using var wrapper = Icon.FromHandle(hIcon);
        Icon icon = (Icon)wrapper.Clone();
        DestroyIcon(hIcon);
        return icon;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    // ── Tray menu ─────────────────────────────────────────────────────────────

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("About",    null, (_, _) => ShowAbout());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit",     null, (_, _) => ExitApp());
        return menu;
    }

    private static void ShowAbout()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "—";
        System.Windows.MessageBox.Show(
            $"ForzaG29Leds  v{v}\n\n" +
            "Drives Logitech G29 / G923 rev-limiter LEDs\n" +
            "from Forza Horizon 6 UDP telemetry.\n\n" +
            "github.com/mathewpalmer17/ForzaG29Leds",
            "About ForzaG29Leds",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, _service!);
        _settingsWindow.SettingsSaved += s =>
        {
            _settings = s;
            _service?.ApplySettings(s);
        };
        _settingsWindow.Show();
    }

    private void ExitApp()
    {
        Shutdown(); // cleanup handled in OnExit
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _service?.Dispose();
        _idleIcon.Dispose();
        _readyIcon.Dispose();
        _liveIcon.Dispose();
        base.OnExit(e);
    }
}
