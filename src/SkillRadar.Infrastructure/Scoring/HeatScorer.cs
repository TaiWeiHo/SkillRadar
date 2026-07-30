using Microsoft.Extensions.Options;
using SkillRadar.Core.Abstractions;
using SkillRadar.Core.Models;

namespace SkillRadar.Infrastructure.Scoring;

/// <summary>
/// v1 scorer: pure arithmetic over star count, freshness, and how many skills a repo curates.
/// No network calls, no external dependencies — safe to run on every batch, including in tests.
/// </summary>
public sealed class HeatScorer : IScorer
{
    private readonly HeatScoringOptions _options;

    public HeatScorer(IOptions<HeatScoringOptions> options)
    {
        _options = options.Value;
    }

    public Task<IReadOnlyList<ScoredSkill>> ScoreAsync(SkillDiffResult diff, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        IReadOnlyList<ScoredSkill> scored = diff.Items
            .Select(item => Score(item, now))
            .OrderByDescending(s => s.Score)
            .ToList();

        return Task.FromResult(scored);
    }

    private ScoredSkill Score(DiffedSkill item, DateTimeOffset now)
    {
        var skill = item.Skill;

        var starScore = _options.StarWeight * Math.Log10(skill.RepoStars + 1);

        var daysSincePush = Math.Max(0, (now - skill.RepoPushedAtUtc).TotalDays);
        var freshnessDecay = Math.Pow(0.5, daysSincePush / Math.Max(1e-6, _options.FreshnessHalfLifeDays));
        var freshnessScore = _options.FreshnessWeight * freshnessDecay;

        var densityScore = _options.DensityWeight * Math.Log10(skill.SkillCountInRepo + 1);

        var total = starScore + freshnessScore + densityScore;

        return new ScoredSkill
        {
            Diffed = item,
            Score = Math.Round(total, 4),
            ScoreRationale =
                $"star={starScore:F2} fresh={freshnessScore:F2} density={densityScore:F2}",
        };
    }
}
