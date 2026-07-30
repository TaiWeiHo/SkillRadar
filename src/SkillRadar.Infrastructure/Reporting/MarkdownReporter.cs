using System.Text;
using Microsoft.Extensions.Options;
using SkillRadar.Core.Abstractions;
using SkillRadar.Core.Models;

namespace SkillRadar.Infrastructure.Reporting;

/// <summary>
/// Builds the daily digest as Markdown. Only builds content in memory — writing it anywhere is
/// IDeliverer's job, so this stays trivially unit-testable and reusable for a future v2 delivery target.
/// </summary>
public sealed class MarkdownReporter : IReporter
{
    private readonly MarkdownReportOptions _options;

    public MarkdownReporter(IOptions<MarkdownReportOptions> options)
    {
        _options = options.Value;
    }

    public Task<DigestReport> BuildReportAsync(
        IReadOnlyList<ScoredSkill> scoredSkills, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var generatedAt = DateTimeOffset.UtcNow;

        var newSkills = scoredSkills.Where(s => s.Status == SkillChangeStatus.New)
            .OrderByDescending(s => s.Score)
            .ToList();
        var updatedCount = scoredSkills.Count(s => s.Status == SkillChangeStatus.Updated);
        var topSkills = scoredSkills.OrderByDescending(s => s.Score).Take(_options.TopN).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# SkillRadar Daily Digest — {today:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine($"_Generated {generatedAt:u}_ · {scoredSkills.Count} skills scanned · " +
                       $"{newSkills.Count} new · {updatedCount} updated");
        sb.AppendLine();

        AppendNewSection(sb, newSkills);
        AppendTopSection(sb, topSkills);
        AppendV2Placeholder(sb);

        var content = sb.ToString();

        var report = new DigestReport
        {
            ReportDate = today,
            MarkdownContent = content,
            NewCount = newSkills.Count,
            UpdatedCount = updatedCount,
            TotalScoredCount = scoredSkills.Count,
            GeneratedAtUtc = generatedAt,
        };

        return Task.FromResult(report);
    }

    private static void AppendNewSection(StringBuilder sb, IReadOnlyList<ScoredSkill> newSkills)
    {
        sb.AppendLine("## 今日新上榜");
        sb.AppendLine();

        if (newSkills.Count == 0)
        {
            sb.AppendLine("_今天沒有新發現的 skill。_");
            sb.AppendLine();
            return;
        }

        foreach (var scored in newSkills)
        {
            AppendSkillLine(sb, scored);
        }

        sb.AppendLine();
    }

    private static void AppendTopSection(StringBuilder sb, IReadOnlyList<ScoredSkill> topSkills)
    {
        sb.AppendLine($"## 熱度 Top {topSkills.Count}");
        sb.AppendLine();

        if (topSkills.Count == 0)
        {
            sb.AppendLine("_目前沒有可排序的 skill。_");
            sb.AppendLine();
            return;
        }

        var rank = 1;
        foreach (var scored in topSkills)
        {
            sb.Append($"{rank}. ");
            AppendSkillLine(sb, scored, includeStatusTag: true);
            rank++;
        }

        sb.AppendLine();
    }

    private static void AppendV2Placeholder(StringBuilder sb)
    {
        sb.AppendLine("## 與我相關的精選 (v2)");
        sb.AppendLine();
        sb.AppendLine("_尚未啟用 LLM 相關性評分（LlmRelevanceScorer）。此區塊留待 v2 實作。_");
        sb.AppendLine();
    }

    private static void AppendSkillLine(StringBuilder sb, ScoredSkill scored, bool includeStatusTag = false)
    {
        var skill = scored.Skill;
        var tag = includeStatusTag ? StatusTag(scored.Status) : string.Empty;

        sb.AppendLine(
            $"- **[{skill.Name}]({skill.HtmlUrl})**{tag} — {skill.Description} " +
            $"_(★{skill.RepoStars} · `{skill.RepoFullName}` · score {scored.Score:F2})_");
    }

    private static string StatusTag(SkillChangeStatus status) => status switch
    {
        SkillChangeStatus.New => " 🆕",
        SkillChangeStatus.Updated => " 🔄",
        _ => string.Empty,
    };
}
