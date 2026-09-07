using HR.Domain.Common;

namespace HR.Domain.Documents;

public enum DocumentStatus
{
    Pending = 0,
    Extracting = 1,
    Cleaning = 2,
    Chunking = 3,
    Embedding = 4,
    Indexing = 5,
    Ready = 6,
    Failed = 7,
}

public sealed class Document
{
    public DocumentId Id { get; set; } = DocumentId.New();
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string SourceFormat { get; set; } = string.Empty; // txt | pdf | json | md
    public DocLanguage Language { get; set; } = DocLanguage.En;
    public string Version { get; set; } = "1.0";
    public string Source { get; set; } = "synthetic";
    public int PageCount { get; set; }
    public HashSet<string> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DocumentStatus Status { get; set; } = DocumentStatus.Pending;
    public string? StatusMessage { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public List<DocumentChunk> Chunks { get; set; } = new();

    public void MarkPreparing(DocumentStatus step) => Status = step;

    public void MarkReady(int chunks)
    {
        Status = DocumentStatus.Ready;
        StatusMessage = $"Indexed {chunks} chunk(s).";
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string error)
    {
        Status = DocumentStatus.Failed;
        StatusMessage = error;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}

public sealed class DocumentChunk
{
    public ChunkId Id { get; set; } = ChunkId.New();
    public DocumentId DocumentId { get; set; }
    public int Ordinal { get; set; }
    public string Section { get; set; } = string.Empty;
    public string PageReference { get; set; } = string.Empty; // "p.3" or "clause 4.2"
    public string Text { get; set; } = string.Empty;
    public DocLanguage Language { get; set; } = DocLanguage.En;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public float[]? Embedding { get; set; }
}

public sealed record Citation(
    DocumentId DocumentId,
    ChunkId ChunkId,
    string DocumentTitle,
    string Section,
    string PageReference,
    string Snippet,
    double Score);
