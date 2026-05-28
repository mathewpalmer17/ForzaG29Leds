using Microsoft.Win32;

namespace ForzaG29Leds;

/// <summary>Manages the "run at Windows startup" registry entry.</summary>
internal static class StartupManager
{
    private const string RegKey  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ForzaG29Leds";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: false);
            return key?.GetValue(AppName) is not null;
        }
    }

    public static void SetEnabled(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegKey, writable: true)!;
        if (enable)
            key.SetValue(AppName, Environment.ProcessPath ?? "");
        else
            key.DeleteValue(AppName, throwOnMissingValue: false);
    }
}
