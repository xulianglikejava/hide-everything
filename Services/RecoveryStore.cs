using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using HideIt.Models;

namespace HideIt.Services;

/// <summary>在当前用户 AppData 下原子保存异常退出恢复状态，避免依赖 exe 所在目录写权限。</summary>
public sealed class RecoveryStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>恢复状态与配置共用产品目录，但使用独立文件避免污染用户配置。</summary>
    public string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppInfo.ProductName,
        "recovery.json");

    /// <summary>读取上次运行留下的恢复状态；损坏或无权限时回退为空状态并允许工具继续启动。</summary>
    public RecoveryState Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new RecoveryState();

            var state = JsonSerializer.Deserialize<RecoveryState>(File.ReadAllText(FilePath), Options)
                ?? new RecoveryState();
            // 旧版本或手工编辑的 JSON 可能把集合写成 null，启动恢复必须按空集合安全处理。
            state.HiddenAppIds ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            state.HiddenWindowRuleIds ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            state.IndividualWindows ??= new List<RecoveryWindow>();
            return state;
        }
        catch (Exception ex)
        {
            Logger.LogException("读取异常退出恢复状态", ex);
            return new RecoveryState();
        }
    }

    /// <summary>
    /// 以临时文件加替换的方式保存状态；写入途中进程崩溃时尽量保留上一份可用状态。
    /// </summary>
    public void Save(RecoveryState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string tempPath = FilePath + ".tmp";
        var json = JsonSerializer.Serialize(state, Options);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, FilePath, overwrite: true);
    }

    /// <summary>正常退出并确认所有窗口恢复后删除恢复标记，避免下一次启动重复隐藏。</summary>
    public void Clear()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
        catch (Exception ex)
        {
            // 清理失败不影响本次退出；保留文件反而能为下次启动提供额外恢复机会。
            Logger.LogException("清理异常退出恢复状态", ex);
        }
    }
}
