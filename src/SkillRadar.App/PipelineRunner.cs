using Microsoft.Extensions.Logging;
using SkillRadar.Core.Abstractions;

namespace SkillRadar.App;

/// <summary>
/// Orchestrates a single end-to-end run: Discovery → Harvest → Diff → Score → Persist → Report → Deliver.
/// Contains no business logic of its own — each stage's real work lives behind its interface. A failure
/// in any stage is logged and ends the run gracefully (non-zero exit) rather than crashing; failures for
/// individual repos are already isolated one level down, inside IDiscoverySource/IHarvester.
/// </summary>
public sealed class PipelineRunner
{
    private readonly IDiscoverySource _discovery;
    private readonly IHarvester _harvester;
    private readonly IStateStore _stateStore;
    private readonly IScorer _scorer;
    private readonly IReporter _reporter;
    private readonly IDeliverer _deliverer;
    private readonly ILogger<PipelineRunner> _logger;

    public PipelineRunner(
        IDiscoverySource discovery,
        IHarvester harvester,
        IStateStore stateStore,
        IScorer scorer,
        IReporter reporter,
        IDeliverer deliverer,
        ILogger<PipelineRunner> logger)
    {
        _discovery = discovery;
        _harvester = harvester;
        _stateStore = stateStore;
        _scorer = scorer;
        _reporter = reporter;
        _deliverer = deliverer;
        _logger = logger;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("SkillRadar run starting");

            var repos = await _discovery.DiscoverRepositoriesAsync(cancellationToken);
            _logger.LogInformation("Discovery found {Count} candidate repos", repos.Count);
            if (repos.Count == 0)
            {
                _logger.LogWarning("No candidate repos found; nothing to harvest. Ending run.");
                return 0;
            }

            var skills = await _harvester.HarvestAsync(repos, cancellationToken);
            _logger.LogInformation("Harvest found {Count} SKILL.md files", skills.Count);
            if (skills.Count == 0)
            {
                _logger.LogWarning("No skills harvested; nothing to score or report. Ending run.");
                return 0;
            }

            var diff = await _stateStore.DiffAsync(skills, cancellationToken);

            var scored = await _scorer.ScoreAsync(diff, cancellationToken);

            await _stateStore.PersistAsync(scored, cancellationToken);

            var report = await _reporter.BuildReportAsync(scored, cancellationToken);

            await _deliverer.DeliverAsync(report, cancellationToken);

            _logger.LogInformation(
                "SkillRadar run complete: {New} new, {Updated} updated, {Total} scored",
                report.NewCount, report.UpdatedCount, report.TotalScoredCount);

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SkillRadar run failed");
            return 1;
        }
    }
}
