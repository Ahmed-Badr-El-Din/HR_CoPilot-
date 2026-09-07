namespace HR.Domain.Screening;

/// <summary>A named competency a job role expects, derived from the rubric dimensions.</summary>
public sealed record Competency(string Name, string Description);

/// <summary>
/// The role the screening targets. Free-form role text is promoted to a typed
/// entity at request time and its competencies are the rubric dimensions, so
/// the entire pipeline speaks the same vocabulary.
/// </summary>
public sealed class JobRole
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<Competency> Competencies { get; set; } = new();
}
