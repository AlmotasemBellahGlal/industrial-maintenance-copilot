using System.Text;
using IndustrialCopilot.Application.Abstractions.Documents;
using IndustrialCopilot.Application.Abstractions.Documents.Models;
using IndustrialCopilot.Application.Knowledge;
using IndustrialCopilot.Infrastructure.Knowledge;
using IndustrialCopilot.Corpus;
namespace IndustrialCopilot.Infrastructure.Tests.Knowledge;

public class FormatPipelineTests
{
    [Fact]
    public async Task PdfExtractionPreservesActualPagesAndChunkMetadataAndIsStable()
    {
        var document = AssessmentCorpus.Generate()[0];
        var extractor = new PdfDocumentExtractor();
        using var source = new MemoryStream(document.Bytes);
        var extracted = await extractor.ExtractAsync(document.Request, source, default);
        Assert.Equal(5, extracted.PageCount);
        Assert.Equal(Enumerable.Range(1, 5), extracted.Segments.Select(s => s.Page!.Value));
        Assert.Contains("seal leakage", extracted.Segments[2].Text);
        Assert.True(source.CanRead);
        var cleaner = new DocumentCleaner(); var chunker = new DeterministicDocumentChunker(500, 50);
        var chunks = chunker.Chunk(document.Request, cleaner.Clean(extracted, default), default);
        var again = chunker.Chunk(document.Request, cleaner.Clean(extracted, default), default);
        Assert.Equal(chunks, again);
        Assert.All(chunks, c => { Assert.Equal(document.Request.Metadata, c.Metadata); Assert.StartsWith($"pdf:page {c.Page};", c.Locator); Assert.NotNull(c.Page); });
        Assert.Equal(chunks.Count, chunks.Select(c => c.ChunkId).Distinct().Count());
        var changedRevision = chunker.Chunk(new(document.Request.DocumentId, Guid.NewGuid(), "application/pdf"), extracted, default);
        Assert.DoesNotContain(changedRevision, c => c.ChunkId == chunks[0].ChunkId);
    }
    [Fact]
    public void PdfIdsLocatorsAndExplicitSectionsAreIndependentOfNarrativeCulture()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var request = new DocumentProcessingRequest(Guid.NewGuid(), Guid.NewGuid(), "application/pdf");
            var document = new ExtractedDocument([new("Section 1: Isolation\nVerify isolation", 1)], 1);
            var chunker = new DeterministicDocumentChunker();
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            var english = Assert.Single(chunker.Chunk(request, document, default));
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("ar-EG");
            var arabic = Assert.Single(chunker.Chunk(request, document, default));
            Assert.Equal(english, arabic); Assert.Equal("Section 1: Isolation", arabic.Section);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = original; }
    }
    [Fact]
    public void CleaningRemovesOnlyBomAndPreservesSourceWhitespaceAndRejectsInvalidControls()
    {
        var cleaner = new DocumentCleaner();
        var input = new ExtractedDocument([new("\uFEFFa  b\r\nc\td")], null);
        Assert.Equal("a  b\r\nc\td", cleaner.Clean(input, default).Segments[0].Text);
        Assert.Throws<DocumentInputException>(() => cleaner.Clean(new([new("a\0b")], null), default));
    }
    [Fact]
    public async Task ChangedContentChangesOnlyAffectedChunkIdentities()
    {
        var pipeline = new DocumentPipeline(new Utf8DocumentExtractor(), new DocumentCleaner(), new DeterministicDocumentChunker(4,0));
        var request = new DocumentProcessingRequest(Guid.NewGuid(), Guid.NewGuid(), "text/plain");
        using var a = new MemoryStream(Encoding.UTF8.GetBytes("abcdefgh")); using var b = new MemoryStream(Encoding.UTF8.GetBytes("abcdijkl"));
        var before = await pipeline.ProcessAsync(request,a,default); var after = await pipeline.ProcessAsync(request,b,default);
        Assert.Equal(before[0].ChunkId,after[0].ChunkId); Assert.NotEqual(before[1].ChunkId,after[1].ChunkId);
    }
    [Fact]
    public async Task MalformedOversizedPdfAndUnsupportedMediaFailClosed()
    {
        var request = new DocumentProcessingRequest(Guid.NewGuid(), Guid.NewGuid(), "application/pdf");
        using var bad = new MemoryStream("not a pdf"u8.ToArray());
        var error = await Assert.ThrowsAsync<DocumentInputException>(()=>new PdfDocumentExtractor().ExtractAsync(request,bad,default));
        Assert.Equal(IngestionFailure.InvalidDocument,error.Failure);
        using var oversized = new MemoryStream(new byte[10]);
        Assert.Equal(IngestionFailure.InputTooLarge,(await Assert.ThrowsAsync<DocumentInputException>(()=>new PdfDocumentExtractor(5).ExtractAsync(request,oversized,default))).Failure);
        await Assert.ThrowsAsync<NotSupportedException>(()=>new ManualDocumentExtractor().ExtractAsync(new(request.DocumentId,request.ManualRevisionId,"application/executable"),Stream.Null,default));
    }
    [Fact]
    public void CorpusMinimumCountsAreCheckedAgainstActualPdfObjects()
    {
        var corpus = AssessmentCorpus.Generate();
        Assert.Equal((31,150),AssessmentCorpus.Validate(corpus));
        Assert.Throws<InvalidOperationException>(()=>AssessmentCorpus.Validate(corpus.Take(29).ToArray()));
        // Enough document identities, insufficient real pages (29 PDFs + text).
        Assert.Throws<InvalidOperationException>(()=>AssessmentCorpus.Validate(corpus.Skip(1).ToArray()));
        Assert.All(corpus,c=>Assert.StartsWith("synthetic:",c.Request.Metadata!.Source));
    }
    [Fact]
    public void ExtractedSegmentsAreDefensivelyOwnedAndCannotFabricateTextPages()
    {
        var segments = new[] {new ExtractedSegment("source")}; var document = new ExtractedDocument(segments,null);
        segments[0]=new("changed"); Assert.Equal("source",document.Segments[0].Text);
        Assert.Throws<ArgumentException>(()=>new ExtractedDocument([new("text",1)],null));
        Assert.Throws<ArgumentException>(()=>new ExtractedDocument([new("text",2)],1));
    }
}
