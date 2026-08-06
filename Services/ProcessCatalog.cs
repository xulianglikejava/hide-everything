using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace HideIt.Services;

/// <summary>A running app with a main window, surfaced in the "Add app" picker.</summary>
public sealed record RunningApp(string ProcessName, string DisplayName, string? ExePath, ImageSource? Icon);

/// <summary>Lists running apps that have a real main window, and extracts their icons.</summary>
public sealed class ProcessCatalog
{
    public IEnumerable<RunningApp> GetRunningAppsWithWindows()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<RunningApp>();

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowHandle == IntPtr.Zero) continue;
                if (string.IsNullOrWhiteSpace(p.MainWindowTitle)) continue;
                if (!seen.Add(p.ProcessName)) continue;

                string? exePath = null;
                try { exePath = p.MainModule?.FileName; }
                catch { /* elevated / bitness mismatch — no path available */ }

                result.Add(new RunningApp(
                    p.ProcessName,
                    DisplayNameFor(p.ProcessName, exePath),
                    exePath,
                    GetIconFor(exePath)));
            }
            catch
            {
                // Process vanished or denied while inspecting — skip it.
            }
            finally
            {
                p.Dispose();
            }
        }

        return result.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase);
    }

    private static string DisplayNameFor(string processName, string? exePath)
    {
        if (!string.IsNullOrEmpty(exePath))
        {
            try
            {
                var fvi = FileVersionInfo.GetVersionInfo(exePath);
                if (!string.IsNullOrWhiteSpace(fvi.FileDescription))
                    return fvi.FileDescription!;
            }
            catch { /* fall back to process name */ }
        }
        return processName;
    }

    /// <summary>Extracts the associated icon as a frozen <see cref="ImageSource"/>, or null on failure.</summary>
    public ImageSource? GetIconFor(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return null;
        try
        {
            using var icon = Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon == null) return null;

            var src = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
    }
}
