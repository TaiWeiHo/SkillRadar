namespace SkillRadar.Core.Models;

/// <summary>The finished daily digest, ready to be delivered by an IDeliverer.</summary>
public sealed record DigestReport
{
    public required DateOnly ReportDate { get; init; }
    public required string MarkdownContent { get; init; }
    public required int NewCount { get; init; }
    public required int UpdatedCount { get; init; }
    public required int TotalScoredCount { get; init; }
    public required DateTimeOffset GeneratedAtUtc { get; init; }
}
