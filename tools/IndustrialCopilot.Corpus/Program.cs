using System.Text.Json;
using IndustrialCopilot.Corpus;
using IndustrialCopilot.Application.Knowledge;
using IndustrialCopilot.Application.Abstractions.Retrieval.Models;
using IndustrialCopilot.Application.Abstractions.Documents;
using IndustrialCopilot.Infrastructure.Knowledge;
using Npgsql;

try
{
var corpus = AssessmentCorpus.Generate();
var counts = AssessmentCorpus.Validate(corpus);
Console.WriteLine($"Synthetic corpus: {counts.Documents} documents, {counts.Pages} actual PDF pages; text file has no page count.");
if (args.Contains("--write"))
{
    var root = Path.GetFullPath("artifacts/corpus"); Directory.CreateDirectory(root);
    foreach (var document in corpus) await File.WriteAllBytesAsync(Path.Combine(root, document.FileName), document.Bytes);
    await File.WriteAllTextAsync(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(corpus.Select(d => new { d.FileName, d.Request }), new JsonSerializerOptions { WriteIndented = true }));
}
if (!args.Contains("--ingest") && !args.Contains("--status")) return;
var connection = Environment.GetEnvironmentVariable("CORPUS_POSTGRES") ?? throw new InvalidOperationException("Set CORPUS_POSTGRES to an isolated assessment database.");
var parsed = new NpgsqlConnectionStringBuilder(connection);
if (parsed.Database != "maintenance_corpus" || parsed.Host is not ("localhost" or "127.0.0.1")) throw new InvalidOperationException("Corpus smoke requires loopback maintenance_corpus database.");
await using var source = NpgsqlDataSource.Create(connection);
if (args.Contains("--ingest")) await new KnowledgeSchema(source).ApplyAsync(default);
var space = new EmbeddingSpace("assessment-corpus-v1", "synthetic-lexical-v1", 256);
var provider = new CorpusEmbeddings();
var store = new PostgresKnowledgeStore(source, new(space, "synthetic-lexical-v1"), provider);
await using var reports = new PostgresIngestionReports(connection);
if (args.Contains("--status"))
{
    foreach (var document in corpus)
        Console.WriteLine(JsonSerializer.Serialize(await reports.ReadAsync(document.Request.DocumentId, document.Request.ManualRevisionId, default),
            new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }));
    return;
}
var service = new ManualIngestionService(new DocumentPipeline(new ManualDocumentExtractor(), new DocumentCleaner(), new DeterministicDocumentChunker()), provider, store, space, reports: reports);
var total = 0;
foreach (var document in corpus)
{
    using var stream = new MemoryStream(document.Bytes);
    total += await service.IngestAsync(document.Request, stream, default);
    var report = (await reports.ReadAsync(document.Request.DocumentId, document.Request.ManualRevisionId, default))[0];
    if (report.State != IngestionState.Completed) throw new InvalidOperationException("Corpus ingestion was not completed.");
}
foreach (var mode in Enum.GetValues<RetrievalMode>())
{
    var doc = corpus[0].Request;
    var hits = await store.RetrieveAsync(new("seal leakage", 3, doc.DocumentId, doc.ManualRevisionId), mode, default);
    if (hits.Count == 0 || hits.Any(h => !h.Locator.StartsWith("pdf:page ") || h.DocumentId != doc.DocumentId || h.ManualRevisionId != doc.ManualRevisionId)) throw new InvalidOperationException("Retrieval provenance smoke failed.");
    Console.WriteLine(JsonSerializer.Serialize(new { Mode = mode.ToString(), Evidence = hits }));
}
Console.WriteLine($"PASS: {counts.Documents} completed documents, {counts.Pages} pages, {total} chunks; keyword/dense/hybrid exact citation smoke.");

}
catch (Exception)
{
    Console.Error.WriteLine("Corpus operation failed. Verify the isolated database and inspect --status for safe per-document stage/categories.");
    Environment.ExitCode = 1;
}
