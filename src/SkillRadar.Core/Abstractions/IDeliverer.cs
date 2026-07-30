using SkillRadar.Core.Models;

namespace SkillRadar.Core.Abstractions;

/// <summary>Ships a finished digest somewhere. v1 writes to disk; v2 can add Notion/LINE without touching IReporter.</summary>
public interface IDeliverer
{
    Task DeliverAsync(DigestReport report, CancellationToken cancellationToken = default);
}
