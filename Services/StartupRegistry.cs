using Microsoft.Win32;

namespace HideIt.Services;

/// <summary>通过当前用户的 HKCU Run 注册表项管理开机后台驻留。</summary>
public sealed class StartupRegistry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HideEverything";

    /// <summary>读取当前用户是否存在 HideEverything 开机启动项；读取失败按未启用处理。</summary>
    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) != null;
        }
        catch (Exception ex)
        {
            // 读取注册表失败时按未启用处理，但不能让托盘工具因此退出。
            Logger.LogException("读取开机启动状态", ex);
            return false;
        }
    }

    /// <summary>
    /// 更新当前用户的开机启动项；成功才返回 true，调用方据此决定是否同步配置和界面状态。
    /// </summary>
    public bool SetEnabled(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null) return false;

            if (on)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exe))
                {
                    Logger.Write("无法设置开机启动：未找到当前 exe 路径。");
                    return false;
                }

                // 通过 --startup 区分登录启动，保证登录后只进入托盘而不弹出设置窗口。
                key.SetValue(
                    ValueName,
                    $"\"{exe}\" {App.StartupArg}",
                    Microsoft.Win32.RegistryValueKind.String);
            }
            else if (key.GetValue(ValueName) != null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            // HKCU 通常无需管理员权限，但策略软件或注册表损坏仍可能拒绝写入。
            Logger.LogException(on ? "启用开机启动" : "关闭开机启动", ex);
            return false;
        }
    }
}
