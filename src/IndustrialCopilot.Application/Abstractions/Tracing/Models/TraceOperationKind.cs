namespace IndustrialCopilot.Application.Abstractions.Tracing.Models;

public enum TraceOperationKind
{
    Orchestration = 1, Agent = 2, Llm = 3, Retrieval = 4, Tool = 5, Approval = 6,
    DocumentProcessing = 7, Indexing = 8
}
