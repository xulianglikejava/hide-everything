using System.Globalization;
using System.Windows;

namespace HideIt.Services;

/// <summary>
/// 管理产品界面的中英文资源。
/// 资源通过应用级字典提供给 XAML 的 DynamicResource，并由代码使用同一套键读取状态提示。
/// </summary>
public static class LocalizationService
{
    /// <summary>简体中文资源标识，也是新配置和异常配置的默认值。</summary>
    public const string ChineseSimplified = "zh-CN";

    /// <summary>英文资源标识。</summary>
    public const string English = "en-US";

    /// <summary>当前已应用的语言标识。</summary>
    public static string CurrentLanguage { get; private set; } = ChineseSimplified;

    /// <summary>资源字典切换完成后通知视图模型刷新派生文案。</summary>
    public static event Action? LanguageChanged;

    /// <summary>把配置中的任意语言值收敛为产品支持的两个标识。</summary>
    public static string Normalize(string? language) =>
        string.Equals(language, English, StringComparison.OrdinalIgnoreCase)
            ? English
            : ChineseSimplified;

    /// <summary>
    /// 替换应用级资源字典并广播语言变化。
    /// 只有本服务创建的 Strings.* 字典会被移除，避免影响未来加入的其他应用资源。
    /// </summary>
    public static void Apply(string? language)
    {
        string normalized = Normalize(language);
        CurrentLanguage = normalized;

        var resources = Application.Current?.Resources;
        if (resources != null)
        {
            for (int i = resources.MergedDictionaries.Count - 1; i >= 0; i--)
            {
                var source = resources.MergedDictionaries[i].Source?.OriginalString;
                if (source?.Contains("/Resources/Strings.", StringComparison.OrdinalIgnoreCase) == true)
                    resources.MergedDictionaries.RemoveAt(i);
            }

            resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Resources/Strings.{normalized}.xaml"),
            });
        }

        LanguageChanged?.Invoke();
    }

    /// <summary>按键读取当前界面文案；资源缺失时返回键名，避免错误提示再次抛异常。</summary>
    public static string Get(string key)
    {
        if (Application.Current?.TryFindResource(key) is string value)
            return value;
        return key;
    }

    /// <summary>使用当前语言格式化带参数的界面文案。</summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
