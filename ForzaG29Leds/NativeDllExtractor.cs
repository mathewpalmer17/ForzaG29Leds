using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace ForzaG29Leds;

/// <summary>
/// For framework-dependent single-file builds: WPF native DLLs are embedded as
/// resources and extracted to %APPDATA%\ForzaG29Leds\native\ on first run.
/// SetDllDirectory ensures they are found before any WPF code initialises.
/// Self-contained builds don't embed these resources so this is a no-op for them.
/// </summary>
internal static class NativeDllExtractor
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    private static readonly string[] DllNames =
    [
        "D3DCompiler_47_cor3.dll",
        "PenImc_cor3.dll",
        "PresentationNative_cor3.dll",
        "vcruntime140_cor3.dll",
        "wpfgfx_cor3.dll",
    ];

    internal static void ExtractAndRegister()
    {
        var asm = Assembly.GetExecutingAssembly();

        // Resources are only embedded in FD builds — SC builds use
        // IncludeNativeLibrariesForSelfExtract and don't need this.
        if (asm.GetManifestResourceInfo(DllNames[0]) is null) return;

        var version = asm.GetName().Version?.ToString() ?? "0";
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ForzaG29Leds", "native", version);

        Directory.CreateDirectory(dir);

        foreach (var name in DllNames)
        {
            var dest = Path.Combine(dir, name);
            if (File.Exists(dest)) continue;   // already extracted this version

            // Write to a temp file first; move atomically when complete.
            // Avoids leaving a corrupt partial file if the process is killed mid-copy.
            var tmp = dest + ".tmp";
            try
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream is null) continue;
                using (var file = File.Create(tmp))
                    stream.CopyTo(file);
                File.Move(tmp, dest, overwrite: true);
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        SetDllDirectory(dir);
    }
}
