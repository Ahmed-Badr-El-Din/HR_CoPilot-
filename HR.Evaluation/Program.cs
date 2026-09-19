using System.Text;
using System.Text.Json;
using HR.Application.Abstractions;
using HR.Application.Abstractions.Persistence;
using HR.Application.Abstractions.Retrieval;
using HR.Application.Workflows;
using HR.Infrastructure;
using HR.Infrastructure.Persistence;
using HR.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Evaluation;

/// <summary>
/// FR-3 evaluation harness entry point. Builds the real infrastructure against a
/// throwaway SQLite database, seeds the synthetic corpus through the production
/// ingestion pipeline, runs the frozen golden + adversarial set and prints honest
/// metrics. Exit code is non-zero if an answerable case is refused, an unanswerable
/// case is answered, or any adversarial payload leaks.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var outputPath = ArgValue(args, "--output") ?? "eval-results.json";
        var dbPath = Path.Combine(Path.GetTempPath(), $"hr-eval-{Guid.NewGuid():N}.db");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:ConnectionString"] = $"Data Source={dbPath}",
                ["Llm:FallbackOrder"] = ArgValue(args, "--provider") ?? "local",
                ["Llm:MaxRetries"] = "1",
                ["Llm:RetryBackoffBaseMs"] = "1",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Error));
        services.AddHrInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider();

        var corpus = CorpusGenerator.Generate();
        var pageCount = corpus.Sum(doc =>
            JsonDocument.Parse(doc.JsonContent).RootElement.GetProperty("pages").GetArrayLength());

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HrDbContext>();
            await db.Database.MigrateAsync();
            var seed = scope.ServiceProvider.GetRequiredService<CorpusSeedService>();
            await seed.SeedAsync();
        }

        var cases = GoldenSet.All;
        EvalReport report;
        await using (var scope = provider.CreateAsyncScope())
        {
            var runner = new EvaluationRunner(
                scope.ServiceProvider.GetRequiredService<IRetrievalService>(),
                scope.ServiceProvider.GetRequiredService<AskService>(),
                scope.ServiceProvider.GetRequiredService<IHrUnitOfWork>(),
                new SystemCorrelationContext($"eval-{Guid.NewGuid():N}"));
            report = await runner.RunAsync(cases, configuration["Llm:FallbackOrder"] ?? "local", corpus.Count, (int)pageCount);
        }

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outputPath, json);
        Console.WriteLine(RenderMarkdown(report));
        Console.WriteLine($"[eval] JSON written to {Path.GetFullPath(outputPath)}");

        TryDelete(dbPath);
        return report.LeakRate > 0 || report.Overall.RefusalAccuracy < 1.0
            ? 1
            : 0;
    }

    private static string RenderMarkdown(EvalReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Evaluation results");
        sb.AppendLine();
        sb.AppendLine($"Generated: {report.GeneratedAt:u}  ");
        sb.AppendLine($"Provider: `{report.Provider}`  ");
        sb.AppendLine($"Corpus: {report.CorpusDocuments} documents / {report.CorpusPages} pages  ");
        sb.AppendLine($"Cases: {report.TotalCases} (adversarial: {report.AdversarialCases})  ");
        sb.AppendLine();
        sb.AppendLine("| Group | Cases | Hit@1 | Hit@5 | MRR | Answer rate | Refusal accuracy | Groundedness | Citation coverage |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var m in report.ByLanguage.Append(report.Overall))
        {
            sb.AppendLine($"| {m.Language} | {m.Cases} | {P(m.HitAt1)} | {P(m.HitAt5)} | {m.Mrr:0.###} | {P(m.AnswerRate)} | {P(m.RefusalAccuracy)} | {P(m.Groundedness)} | {P(m.CitationCoverage)} |");
        }

        sb.AppendLine();
        sb.AppendLine($"Direct-injection refusal rate: **{P(report.DirectInjectionRefusalRate)}**  ");
        sb.AppendLine($"Adversarial leak rate: **{P(report.LeakRate)}**  ");
        sb.AppendLine($"Average latency: **{report.AverageLatencyMs} ms**");
        return sb.ToString();
    }

    private static string P(double value) => $"{value * 100:0.#}%";

    private static string? ArgValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.Ordinal))
                return args[i + 1];
        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Temporary evaluation database; best-effort cleanup only.
        }
    }
}
