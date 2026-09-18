using HR.Infrastructure.Prompts;
using Xunit;

namespace HR.Tests;

public sealed class EmbeddedPromptCatalogTests
{
    [Fact]
    public void Loads_every_versioned_prompt_asset()
    {
        var catalog = new EmbeddedPromptCatalog();
        var assets = catalog.List();

        Assert.Contains(assets, a => a.Id == "chat/grounded-answer" && a.Version == "v1");
        Assert.Contains(assets, a => a.Id == "agents/rubric-scoring" && a.Version == "v1");
        Assert.Contains(assets, a => a.Id == "agents/evidence-extraction" && a.Version == "v1");
        Assert.Contains(assets, a => a.Id == "agents/shortlist-drafting" && a.Version == "v1");
    }

    [Fact]
    public void Resolves_prompt_body_by_id_and_version()
    {
        var catalog = new EmbeddedPromptCatalog();
        var body = catalog.GetPrompt("chat/grounded-answer", 1);

        Assert.False(string.IsNullOrWhiteSpace(body));
        Assert.Contains("corpus", body, StringComparison.OrdinalIgnoreCase);
    }
}
