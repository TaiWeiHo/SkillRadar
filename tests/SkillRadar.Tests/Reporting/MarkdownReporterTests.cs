using Microsoft.Extensions.Options;
using SkillRadar.Core.Models;
using SkillRadar.Infrastructure.Reporting;
using Xunit;

namespace SkillRadar.Tests.Reporting;

public class MarkdownReporterTests
{
    private static ScoredSkill MakeScored(string name, SkillChangeStatus status, double score) => new()
    {
        Diffed = new DiffedSkill
        {
            Skill = new SkillRecord
            {
                RepoFullName = "owner/repo",
                Path = "SKILL.md",
                HtmlUrl = $"https://github.com/owner/repo/blob/main/SKILL.md",
                Name = name,
                Description = "a test skill",
                ContentHash = "hash",
                RepoStars = 10,
                RepoPushedAtUtc = DateTimeOffset.UtcNow,
                SkillCountInRepo = 1,
            },
            Status = status,
            FirstSeenUtc = DateTimeOffset.UtcNow,
        },
        Score = score,
    };

    [Fact]
    public async Task BuildReportAsync_TopNSection_UsesCleanNumberedList_NoBulletDash()
    {
        var reporter = new MarkdownReporter(Options.Create(new MarkdownReportOptions { TopN = 5 }));
        var scored = new[] { MakeScored("skill-a", SkillChangeStatus.New, 10.0) };

        var report = await reporter.BuildReportAsync(scored);

        Assert.Contains("1. **[skill-a]", report.MarkdownContent);
        Assert.DoesNotContain("1. - **", report.MarkdownContent);
    }

    [Fact]
    public async Task BuildReportAsync_NewSkillsSection_StillUsesBulletList()
    {
        var reporter = new MarkdownReporter(Options.Create(new MarkdownReportOptions { TopN = 5 }));
        var scored = new[] { MakeScored("skill-a", SkillChangeStatus.New, 10.0) };

        var report = await reporter.BuildReportAsync(scored);

        var newSection = report.MarkdownContent.Split("## 熱度")[0];
        Assert.Contains("- **[skill-a]", newSection);
    }
}
