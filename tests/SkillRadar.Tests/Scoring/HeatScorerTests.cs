using Microsoft.Extensions.Options;
using SkillRadar.Core.Models;
using SkillRadar.Infrastructure.Scoring;
using Xunit;

namespace SkillRadar.Tests.Scoring;

public class HeatScorerTests
{
    private static HeatScorer CreateScorer(HeatScoringOptions? options = null) =>
        new(Options.Create(options ?? new HeatScoringOptions()));

    private static DiffedSkill MakeSkill(
        string name,
        int stars,
        double daysSincePush,
        int skillCountInRepo = 1,
        SkillChangeStatus status = SkillChangeStatus.Unchanged)
    {
        var skill = new SkillRecord
        {
            RepoFullName = $"owner/{name}",
            Path = "SKILL.md",
            HtmlUrl = $"https://github.com/owner/{name}/blob/main/SKILL.md",
            Name = name,
            Description = "a test skill",
            ContentHash = "hash",
            RepoStars = stars,
            RepoPushedAtUtc = DateTimeOffset.UtcNow.AddDays(-daysSincePush),
            SkillCountInRepo = skillCountInRepo,
        };

        return new DiffedSkill { Skill = skill, Status = status, FirstSeenUtc = DateTimeOffset.UtcNow };
    }

    [Fact]
    public async Task ScoreAsync_RanksMoreStarredRepoHigher_AllElseEqual()
    {
        var diff = new SkillDiffResult
        {
            Items =
            [
                MakeSkill("popular", stars: 5000, daysSincePush: 1),
                MakeSkill("obscure", stars: 5, daysSincePush: 1),
            ],
        };

        var scored = await CreateScorer().ScoreAsync(diff);

        Assert.Equal("popular", scored[0].Skill.Name);
        Assert.Equal("obscure", scored[1].Skill.Name);
        Assert.True(scored[0].Score > scored[1].Score);
    }

    [Fact]
    public async Task ScoreAsync_RanksFresherRepoHigher_AllElseEqual()
    {
        var diff = new SkillDiffResult
        {
            Items =
            [
                MakeSkill("fresh", stars: 100, daysSincePush: 0),
                MakeSkill("stale", stars: 100, daysSincePush: 365),
            ],
        };

        var scored = await CreateScorer().ScoreAsync(diff);

        Assert.Equal("fresh", scored[0].Skill.Name);
        Assert.Equal("stale", scored[1].Skill.Name);
    }

    [Fact]
    public async Task ScoreAsync_RewardsHigherSkillDensityInRepo()
    {
        var diff = new SkillDiffResult
        {
            Items =
            [
                MakeSkill("curated", stars: 100, daysSincePush: 1, skillCountInRepo: 10),
                MakeSkill("single", stars: 100, daysSincePush: 1, skillCountInRepo: 1),
            ],
        };

        var scored = await CreateScorer().ScoreAsync(diff);

        Assert.Equal("curated", scored[0].Skill.Name);
        Assert.Equal("single", scored[1].Skill.Name);
    }

    [Fact]
    public async Task ScoreAsync_ZeroWeightDisablesThatDimension()
    {
        var options = new HeatScoringOptions { StarWeight = 0, FreshnessWeight = 0, DensityWeight = 1 };
        var diff = new SkillDiffResult
        {
            Items =
            [
                MakeSkill("many-skills", stars: 1, daysSincePush: 999, skillCountInRepo: 50),
                MakeSkill("huge-stars", stars: 1_000_000, daysSincePush: 0, skillCountInRepo: 1),
            ],
        };

        var scored = await CreateScorer(options).ScoreAsync(diff);

        Assert.Equal("many-skills", scored[0].Skill.Name);
    }

    [Fact]
    public async Task ScoreAsync_ReturnsResultsOrderedDescendingByScore()
    {
        var diff = new SkillDiffResult
        {
            Items =
            [
                MakeSkill("low", stars: 1, daysSincePush: 999),
                MakeSkill("high", stars: 99999, daysSincePush: 0),
                MakeSkill("mid", stars: 100, daysSincePush: 10),
            ],
        };

        var scored = await CreateScorer().ScoreAsync(diff);

        Assert.Equal(new[] { "high", "mid", "low" }, scored.Select(s => s.Skill.Name));
        Assert.True(scored[0].Score >= scored[1].Score);
        Assert.True(scored[1].Score >= scored[2].Score);
    }
}
