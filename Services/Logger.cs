using System.IO;

namespace HideIt.Services;

/// <summary>把崩溃和诊断信息追加写入当前用户 AppData 下的 HideEverything\logs。</summary>
public static class Logger
{
    private static readonly object Gate = new();
    private static string _logFile = "";

    // 日志与配置使用同一产品目录，且不依赖 exe 所在目录的写权限。
    public static string LogDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.ProductName, "logs");

    public static void Init()
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            _logFile = Path.Combine(LogDir, $"hideeverything-{DateTime.Now:yyyyMMdd}.log");
        }
        catch { /* logging must never crash the app */ }
    }

    public static void LogException(string context, Exception? ex) =>
        Write($"[{context}] {ex}");

    public static void Write(string message)
    {
        if (string.IsNullOrEmpty(_logFile)) return;
        try
        {
            lock (Gate)
                File.AppendAllText(_logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch { /* swallow */ }
    }
}
