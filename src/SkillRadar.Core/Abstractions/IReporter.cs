using SkillRadar.Core.Models;

namespace SkillRadar.Core.Abstractions;

/// <summary>Builds the daily digest content. Does not decide where it ends up — see IDeliverer.</summary>
public interface IReporter
{
    Task<DigestReport> BuildReportAsync(
        IReadOnlyList<ScoredSkill> scoredSkills,
        CancellationToken cancellationToken = default);
}
