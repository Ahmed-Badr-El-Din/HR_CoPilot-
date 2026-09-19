using HR.Domain.Common;

namespace HR.Domain.Screening;

/// <summary>A scoring dimension on the rubric. One of the four protected-dimension rules:
/// scores are computed only from redacted evidence, never from protected attributes.</summary>
public sealed class RubricDimension
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Weight { get; set; } = 1.0;
    public int MaxScore { get; set; } = 10;
}

public sealed class Rubric
{
    public string Name { get; set; } = string.Empty;
    public List<RubricDimension> Dimensions { get; set; } = new();
}

public sealed class CandidateRef
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DocumentId DocumentId { get; set; }
}

public sealed class ScreeningRequest
{
    public JobRole Role { get; set; } = new();
    public Rubric Rubric { get; set; } = new();
    public List<CandidateRef> Candidates { get; set; } = new();
}

/// <summary>Returns the weighted total for a set of dimension scores, capping illegal weights.</summary>
public static class RubricMath
{
    public static double WeightedTotal(IEnumerable<(string DimensionId, double Percent, double Max)> scores)
    {
        var sum = 0.0;
        double weightSum = 0.0;
        foreach (var (_, percent, max) in scores)
        {
            sum += percent * max;
            weightSum += percent;
        }

        return weightSum <= 0 ? 0 : sum / weightSum;
    }
}
