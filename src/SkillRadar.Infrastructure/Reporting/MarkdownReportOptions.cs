namespace SkillRadar.Infrastructure.Reporting;

/// <summary>Bound from the "SkillRadar:Reporting" configuration section.</summary>
public sealed class MarkdownReportOptions
{
    public const string SectionName = "SkillRadar:Reporting";

    /// <summary>How many skills to list in the "熱度 Top N" section.</summary>
    public int TopN { get; init; } = 20;

    /// <summary>Directory reports are written to by FileDeliverer, relative to the working directory unless rooted.</summary>
    public string OutputDirectory { get; init; } = "reports";
}
