namespace SkillRadar.Core.Models;

/// <summary>A GitHub repository surfaced by discovery as a candidate to harvest.</summary>
public sealed record RepoInfo
{
    public required string FullName { get; init; }
    public required string HtmlUrl { get; init; }
    public required int Stars { get; init; }
    public required DateTimeOffset PushedAtUtc { get; init; }
    public required string DefaultBranch { get; init; }
}
