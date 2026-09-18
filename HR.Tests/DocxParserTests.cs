using System.IO.Compression;
using System.Text;
using HR.Infrastructure.Processing;
using Xunit;

namespace HR.Tests;

public sealed class DocxParserTests
{
    private const string DocumentXml =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:body>
            <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Experience</w:t></w:r></w:p>
            <w:p><w:r><w:t xml:space="preserve">Built bilingual data pipelines with SQL and Python.</w:t></w:r></w:p>
            <w:p><w:pPr><w:pStyle w:val="Heading2"/></w:pPr><w:r><w:t>Education</w:t></w:r></w:p>
            <w:p><w:r><w:t>MSc Statistics.</w:t></w:r></w:p>
          </w:body>
        </w:document>
        """;

    private static MemoryStream BuildDocx()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(DocumentXml);
        }

        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task Parses_docx_into_sections_and_pages()
    {
        using var stream = BuildDocx();
        var document = await new DocxTextParser().ParseAsync(stream, "candidate-cv.docx");

        Assert.Equal("docx", document.SourceFormat);
        Assert.NotEmpty(document.Pages);
        var text = string.Join("\n", document.Pages.Select(p => p.Text));
        Assert.Contains("bilingual data pipelines", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(document.Pages, p => p.Section == "Experience");
        Assert.Contains(document.Pages, p => p.Section == "Education");
    }

    [Fact]
    public void Composite_parser_advertises_docx()
    {
        var parser = new CompositeDocumentParser(new PdfTextParser(), new DocxTextParser());
        Assert.True(parser.SupportsFormat("cv.docx"));
        Assert.True(parser.SupportsFormat("spec.PDF"));
        Assert.False(parser.SupportsFormat("spec.pages"));
    }
}
