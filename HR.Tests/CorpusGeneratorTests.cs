using System.Text.Json;
using HR.Infrastructure.Seed;
using Xunit;

namespace HR.Tests;

public sealed class CorpusGeneratorTests
{
    [Fact]
    public void Produces_at_least_thirty_synthetic_documents()
    {
        var docs = CorpusGenerator.Generate();
        Assert.True(docs.Count >= 30, $"corpus has {docs.Count} docs; expected >= 30 for the D6 evaluation harness");
    }

    [Fact]
    public void Produces_at_least_one_hundred_and_fifty_pages()
    {
        var pages = CorpusGenerator.Generate()
            .Sum(d =>
            {
                using var json = JsonDocument.Parse(d.JsonContent);
                return json.RootElement.GetProperty("pages").GetArrayLength();
            });
        Assert.True(pages >= 150, $"corpus has {pages} pages; the D6 brief requires >= 150");
    }

    [Fact]
    public void Ships_adversarial_prompt_injection_fixtures()
    {
        var docs = CorpusGenerator.Generate();
        Assert.Contains(docs, d => d.FileName.StartsWith("adversarial-injection-", StringComparison.Ordinal));
        Assert.True(docs.Count(d => d.FileName.StartsWith("adversarial-injection-", StringComparison.Ordinal)) >= 2);
    }

    [Fact]
    public void Is_bilingual()
    {
        var docs = CorpusGenerator.Generate();
        var arabic = 0;
        var english = 0;
        foreach (var doc in docs)
        {
            using var json = JsonDocument.Parse(doc.JsonContent);
            var lang = json.RootElement.TryGetProperty("language", out var l) ? l.GetString() : null;
            if (lang == "ar") arabic++;
            if (lang == "en") english++;
        }

        Assert.True(arabic >= 10, $"expected >= 10 Arabic docs, got {arabic}");
        Assert.True(english >= 10, $"expected >= 10 English docs, got {english}");
        Assert.Contains(docs, d => d.FileName.Contains("candidate-cv-", StringComparison.Ordinal));
        Assert.Contains(docs, d => d.FileName.Contains("role-", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_document_is_valid_versioned_json_with_pages()
    {
        foreach (var doc in CorpusGenerator.Generate())
        {
            using var json = JsonDocument.Parse(doc.JsonContent);
            var root = json.RootElement;
            Assert.True(root.TryGetProperty("title", out _), $"{doc.FileName} lacks title");
            Assert.True(root.TryGetProperty("version", out _), $"{doc.FileName} lacks version");
            Assert.True(root.TryGetProperty("language", out _), $"{doc.FileName} lacks language");
            Assert.True(root.GetProperty("pages").GetArrayLength() >= 1, $"{doc.FileName} has no pages");
        }
    }

    [Fact]
    public void Is_deterministic_across_runs()
    {
        var first = CorpusGenerator.Generate();
        var second = CorpusGenerator.Generate();
        Assert.Equal(
            first.Select(d => (d.FileName, d.JsonContent)),
            second.Select(d => (d.FileName, d.JsonContent)));
    }
}
