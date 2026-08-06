using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using HideIt.Models;

namespace HideIt.Services;

/// <summary>在当前用户 AppData 的 HideEverything 目录读写缩进 JSON 配置。</summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // 配置必须落在用户目录，避免便携 exe 因安装目录不可写而要求管理员权限。
    public string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.ProductName);

    public string FilePath => Path.Combine(Dir, "config.json");

    /// <summary>读取持久配置；文件缺失、为空或损坏时返回带产品默认值的新配置。</summary>
    public AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, Options);
                if (cfg != null)
                {
                    // 语言字段是可选的兼容字段，旧配置继续使用简体中文。
                    cfg.Language = LocalizationService.Normalize(cfg.Language);
                    return cfg;
                }
            }
        }
        catch
        {
            // 配置损坏时回退到默认配置，不能让用户因本地 JSON 问题无法启动托盘工具。
        }
        return new AppConfig();
    }

    /// <summary>创建用户配置目录并以缩进 JSON 保存当前配置，便于用户诊断和备份。</summary>
    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(config, Options);
        File.WriteAllText(FilePath, json);
    }
}
