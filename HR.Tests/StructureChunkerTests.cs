using HR.Application.Abstractions.Processing;
using HR.Domain.Common;
using HR.Infrastructure.Processing;
using Xunit;

namespace HR.Tests;

public sealed class StructureChunkerTests
{
    private static ParsedDocument Document(string heading, IReadOnlyList<string> pageOneLines, IReadOnlyList<string> pageTwoLines)
        => new(
            Title: "cv",
            FileName: "cv.txt",
            SourceFormat: "txt",
            Language: DocLanguage.En,
            Version: "1.0",
            Source: "synthetic",
            Pages: new[]
            {
                new ParsedPage(1, heading, $"{heading}\n{string.Join('\n', pageOneLines)}"),
                new ParsedPage(2, heading, string.Join('\n', pageTwoLines)),
            });

    [Fact]
    public void Preserves_section_and_page_reference()
    {
        var doc = Document(
            "# Experience",
            Enumerable.Repeat("Analysed large datasets with SQL and Power BI for weekly stakeholder reports, running weekly cadenced reviews.", 6).ToArray(),
            Enumerable.Repeat("Led a four-person analytics team and owned the ninety-thousand dollar tooling budget each year.", 4).ToArray());

        var segments = new StructureChunker().Chunk(doc, maxCharacters: 160);

        Assert.True(segments.Count >= 2, $"expected chunks on both pages, got {segments.Count}");
        Assert.All(segments, s => Assert.Equal("Experience", s.Section));
        Assert.Contains(segments, s => s.PageReference == "p.1");
        Assert.Contains(segments, s => s.PageReference == "p.2");
        Assert.Contains(segments, s => s.Text.Contains("Power BI", StringComparison.Ordinal));
    }

    [Fact]
    public void Sections_are_split_when_heading_changes()
    {
        var doc = new ParsedDocument(
            Title: "role",
            FileName: "role.txt",
            SourceFormat: "txt",
            Language: DocLanguage.En,
            Version: "1.0",
            Source: "synthetic",
            Pages: new[]
            {
                new ParsedPage(1, string.Empty, "# Responsibilities\nLead a team of analysts and own stakeholder reporting."),
                new ParsedPage(1, string.Empty, "# Qualifications\nSeven years in analytics with SQL and Python."),
            });

        var segments = new StructureChunker().Chunk(doc, maxCharacters: 1000);

        var sections = segments.Select(s => s.Section).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Responsibilities", sections);
        Assert.Contains("Qualifications", sections);
    }

    [Fact]
    public void Long_content_is_capped_into_multiple_chunks()
    {
        var lines = Enumerable.Repeat("Analysed large datasets with SQL and Python building reports for stakeholders each week.", 12).ToArray();
        var doc = Document("# Experience", lines, Array.Empty<string>());

        var segments = new StructureChunker().Chunk(doc, maxCharacters: 150);

        Assert.True(segments.Count >= 2, $"expected multiple chunks, got {segments.Count}");
        Assert.True(segments.All(s => s.Text.Length <= 380), "sentence-boundary cut may add a small remainder, but never a huge chunk");
    }
}
