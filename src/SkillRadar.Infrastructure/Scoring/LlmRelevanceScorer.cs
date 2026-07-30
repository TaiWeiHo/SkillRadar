using Microsoft.Extensions.Options;
using SkillRadar.Core.Abstractions;
using SkillRadar.Core.Models;

namespace SkillRadar.Infrastructure.Scoring;

/// <summary>
/// v2 STUB — not implemented, not registered in DI by default. Intent: take the top N heat-scored
/// skills, send each description to Claude (ANTHROPIC_API_KEY from env), and re-rank/tag them with a
/// 0-10 relevance score plus a one-line Traditional Chinese summary. Only score a pre-filtered list
/// (see LlmRelevanceOptions.MaxItemsToScore) to keep API cost bounded — never score the raw firehose.
/// </summary>
public sealed class LlmRelevanceScorer : IScorer
{
    private readonly LlmRelevanceOptions _options;

    public LlmRelevanceScorer(IOptions<LlmRelevanceOptions> options)
    {
        _options = options.Value;
    }

    public Task<IReadOnlyList<ScoredSkill>> ScoreAsync(SkillDiffResult diff, CancellationToken cancellationToken = default)
    {
        // TODO(v2): call the Anthropic Messages API (model: _options.Model) for the top
        // _options.MaxItemsToScore heat-scored items, parse a 0-10 relevance score + zh-TW summary,
        // and fold that into ScoredSkill (e.g. via ScoreRationale or a new field). Read the key from
        // ANTHROPIC_API_KEY only — never from appsettings.json.
        throw new NotImplementedException("LlmRelevanceScorer is a v2 stub; wire it up when relevance scoring is in scope.");
    }
}
