using System.Text;
using HR.Application.Abstractions.Processing;

namespace HR.Infrastructure.Processing;

/// <summary>
/// Structural chunker (chunking strategy chosen in ADR-002): chunks follow the
/// document's section + page boundaries, then are capped by a maximum character
/// count. This keeps citations precise ("p.3 — clause 4.2") instead of scattering
/// paragraphs across fixed-size windows. Headings become section metadata, which
/// the retrieval pipeline exposes to the UI.
/// </summary>
public sealed class StructureChunker : IDocumentChunker
{
    public IReadOnlyList<ChunkSegment> Chunk(ParsedDocument document, int maxCharacters)
    {
        var segments = new List<ChunkSegment>();
        var buffer = new StringBuilder();
        string currentSection = string.Empty;
        int bufferPage = 0;

        void Flush()
        {
            if (buffer.Length == 0) return;
            var pageRef = bufferPage > 0 ? $"p.{bufferPage}" : string.Empty;
            segments.Add(new ChunkSegment(currentSection, pageRef, buffer.ToString().Trim()));
            buffer.Clear();
        }

        foreach (var page in document.Pages)
        {
            foreach (var line in page.Text.Replace("\r\n", "\n").Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;

                if (CompositeDocumentParser.LooksLikeHeading(trimmed))
                {
                    Flush();
                    currentSection = trimmed.TrimStart('#', ' ').TrimEnd(':');
                    continue;
                }

                if (buffer.Length == 0) bufferPage = page.Number;
                buffer.Append(trimmed).Append(' ');
                if (buffer.Length > maxCharacters)
                {
                    // Close on a sentence boundary if one exists within the buffer.
                    var text = buffer.ToString();
                    if (text.Length > 220)
                    {
                        var cut = text.AsSpan(0, text.Length - 90).LastIndexOf('.');
                        if (cut >= 140 && cut < text.Length - 40)
                        {
                            segments.Add(new ChunkSegment(currentSection, bufferPage > 0 ? $"p.{bufferPage}" : string.Empty, text[..(cut + 1)].Trim()));
                            buffer.Clear();
                            buffer.Append(text[(cut + 1)..].TrimStart()).Append(' ');
                            bufferPage = page.Number;
                            continue;
                        }
                    }

                    Flush();
                    bufferPage = page.Number;
                }
            }
        }

        Flush();

        if (segments.Count == 0)
        {
            var flat = string.Join(" ", document.Pages.Select(p => p.Text));
            if (!string.IsNullOrWhiteSpace(flat))
                segments.Add(new ChunkSegment(currentSection, string.Empty, flat.Trim()));
        }

        return segments;
    }
}
