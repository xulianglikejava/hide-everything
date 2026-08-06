namespace HideIt;

/// <summary>集中维护产品名称和 GitHub 链接，避免界面入口与发布地址不一致。</summary>
public static class AppInfo
{
    public const string ProductName = "HideEverything";

    public const string RepoOwner = "xulianglikejava";
    public const string RepoName = "hide-everything";

    public static string RepoUrl => $"https://github.com/{RepoOwner}/{RepoName}";
    public static string ReleasesUrl => $"{RepoUrl}/releases";
}
