using System.IO;
using System.Runtime.InteropServices;

namespace HideIt.Services;

/// <summary>Creates .lnk shortcuts to the running exe on the Desktop / Start Menu.</summary>
public static class ShortcutService
{
    public static string DesktopPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppInfo.ProductName}.lnk");

    public static string StartMenuPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), $"{AppInfo.ProductName}.lnk");

    public static bool CreateDesktopShortcut() => Create(DesktopPath);

    public static bool CreateStartMenuShortcut() => Create(StartMenuPath);

    private static bool Create(string lnkPath)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;

        object? shell = null;
        object? shortcut = null;
        try
        {
            // Use the Windows Script Host COM object (no extra reference needed).
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return false;

            shell = Activator.CreateInstance(type);
            if (shell == null) return false;

            dynamic sh = shell;
            shortcut = sh.CreateShortcut(lnkPath);
            dynamic sc = shortcut!;
            sc.TargetPath = exe;
            sc.WorkingDirectory = Path.GetDirectoryName(exe) ?? "";
            sc.Description = AppInfo.ProductName;
            sc.IconLocation = exe + ",0";
            sc.Save();
            return File.Exists(lnkPath);
        }
        catch (Exception ex)
        {
            Logger.LogException($"CreateShortcut {lnkPath}", ex);
            return false;
        }
        finally
        {
            if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
            if (shell != null) Marshal.FinalReleaseComObject(shell);
        }
    }
}
