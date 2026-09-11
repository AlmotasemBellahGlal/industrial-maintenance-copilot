using IndustrialCopilot.Application.Abstractions.Agents;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Tracing;
using IndustrialCopilot.Application.Abstractions.Tracing.Models;

namespace IndustrialCopilot.Application.Tests.Abstractions.Tracing;

public class TraceContractTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
    private static TraceStep Step(Guid? parent = null, TraceOperationKind kind = TraceOperationKind.Orchestration,
        TraceStepStatus status = TraceStepStatus.Running, TokenUsage? usage = null, MonetaryCost? cost = null) =>
        new(Guid.NewGuid(), parent, kind, "operation", Start, status,
            status is TraceStepStatus.Running or TraceStepStatus.Waiting ? null : Start.AddSeconds(2),
            status == TraceStepStatus.Failed ? new("execution_failed", "Operation failed") : null,
            kind == TraceOperationKind.Agent ? AgentRole.SymptomMatcher : null, usage, cost);
    private static RunTraceSnapshot Run(params TraceStep[] steps) => new(Guid.NewGuid(), Guid.NewGuid(), null, 1, steps);

    [Fact]
    public void IdentitiesAndStorageVersionAreValidated()
    {
        var root = Step();
        Assert.Throws<ArgumentException>(() => new RunTraceSnapshot(Guid.Empty, Guid.NewGuid(), null, 1, [root]));
        Assert.Throws<ArgumentException>(() => new RunTraceSnapshot(Guid.NewGuid(), Guid.Empty, null, 1, [root]));
        Assert.Throws<ArgumentException>(() => new RunTraceSnapshot(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, 1, [root]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RunTraceSnapshot(Guid.NewGuid(), Guid.NewGuid(), null, 0, [root]));
        Assert.Throws<ArgumentException>(() => new TraceStep(Guid.Empty, null, TraceOperationKind.Tool, "tool", Start, TraceStepStatus.Running));
        Assert.Throws<ArgumentException>(() => new TraceStep(Guid.NewGuid(), Guid.Empty, TraceOperationKind.Tool, "tool", Start, TraceStepStatus.Running));
        Assert.Throws<ArgumentException>(() => new TraceStep(root.StepId, root.StepId, TraceOperationKind.Tool, "tool", Start, TraceStepStatus.Running));
    }

    [Fact]
    public void TreeRejectsDuplicatesMissingParentsCyclesAndMultipleRoots()
    {
        var root = Step();
        Assert.Throws<ArgumentException>(() => Run());
        Assert.Throws<ArgumentNullException>(() => Run(null!));
        Assert.Throws<ArgumentException>(() => Run(root, null!));
        Assert.Throws<ArgumentException>(() => Run(root, root));
        Assert.Throws<ArgumentException>(() => Run(root, Step()));
        Assert.Throws<ArgumentException>(() => Run(root, Step(Guid.NewGuid())));
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => Run(root,
            new(a, b, TraceOperationKind.Tool, "a", Start, TraceStepStatus.Running),
            new(b, a, TraceOperationKind.Tool, "b", Start, TraceStepStatus.Running)));
    }

    [Theory]
    [InlineData(TraceStepStatus.Completed)]
    [InlineData(TraceStepStatus.Cancelled)]
    [InlineData(TraceStepStatus.Failed)]
    public void TerminalExecutionHasEndAndRemainsObservational(TraceStepStatus status)
    {
        var step = Step(status: status);
        var run = Run(step);
        Assert.Equal(status, run.Root.Status);
        Assert.Equal(TimeSpan.FromSeconds(2), step.Duration);
        Assert.Null(run.MaintenanceRunId);
        Assert.Throws<ArgumentException>(() => new TraceStep(Guid.NewGuid(), null, TraceOperationKind.Orchestration,
            "run", Start, status, error: status == TraceStepStatus.Failed ? new("failure", "Safe explanation") : null));
    }

    [Fact]
    public void TimestampsAndErrorsCannotContradictStatus()
    {
        Assert.Throws<ArgumentException>(() => new TraceStep(Guid.NewGuid(), null, TraceOperationKind.Llm, "call", default, TraceStepStatus.Running));
        Assert.Throws<ArgumentException>(() => new TraceStep(Guid.NewGuid(), null, TraceOperationKind.Llm, "call", Start, TraceStepStatus.Completed, Start.AddSeconds(-1)));
        Assert.Throws<ArgumentException>(() => new TraceStep(Guid.NewGuid(), null, TraceOperationKind.Llm, "call", Start, TraceStepStatus.Running, Start));
        Assert.Throws<ArgumentException>(() => new TraceStep(Guid.NewGuid(), null, TraceOperationKind.Llm, "call", Start, TraceStepStatus.Failed, Start));
        Assert.Throws<ArgumentException>(() => new TraceStep(Guid.NewGuid(), null, TraceOperationKind.Llm, "call", Start, TraceStepStatus.Completed, Start, new("error", "safe")));
        Assert.Throws<ArgumentException>(() => new TraceError(" ", "safe"));
        Assert.Throws<ArgumentException>(() => new TraceError("error", " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TraceStep(Guid.NewGuid(), null, (TraceOperationKind)999, "op", Start, TraceStepStatus.Running));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TraceStep(Guid.NewGuid(), null, TraceOperationKind.Llm, "op", Start, (TraceStepStatus)999));
    }

    [Fact]
    public void ChildTimingMustFitParentAndTerminalParentCannotContainActiveChild()
    {
        var root = Step(status: TraceStepStatus.Completed);
        Assert.Throws<ArgumentException>(() => Run(root, Step(root.StepId)));
        Assert.Throws<ArgumentException>(() => Run(root, new(Guid.NewGuid(), root.StepId, TraceOperationKind.Tool, "tool", Start.AddSeconds(-1), TraceStepStatus.Completed, Start)));
        Assert.Throws<ArgumentException>(() => Run(root, new(Guid.NewGuid(), root.StepId, TraceOperationKind.Tool, "tool", Start, TraceStepStatus.Completed, Start.AddSeconds(3))));
        Assert.Equal(2, Run(root, Step(root.StepId, status: TraceStepStatus.Completed)).Steps.Count);
    }

    [Fact]
    public void IncrementalTreeOwnsItsCollectionAndShowsApprovalAndToolOperations()
    {
        var root = Step();
        var agent = Step(root.StepId, TraceOperationKind.Agent);
        var tool = Step(agent.StepId, TraceOperationKind.Tool);
        var approval = new TraceStep(Guid.NewGuid(), root.StepId, TraceOperationKind.Approval, "supervisor_review",
            Start, TraceStepStatus.Waiting, workOrderId: Guid.NewGuid(), workOrderRevision: 3);
        var input = new List<TraceStep> { tool, agent, approval, root };
        var run = new RunTraceSnapshot(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, input);
        input.Clear();
        Assert.Equal(4, run.Steps.Count);
        Assert.Same(root, run.Root);
        Assert.Null(approval.CompletedAt);
        Assert.Null(approval.Duration);
        Assert.Equal(AgentRole.SymptomMatcher, agent.AgentRole);
        Assert.Throws<NotSupportedException>(() => ((IList<TraceStep>)run.Steps).Clear());
        Assert.Throws<ArgumentException>(() => new TraceStep(Guid.NewGuid(), null, TraceOperationKind.Tool, "tool", Start,
            TraceStepStatus.Running, agentRole: AgentRole.SymptomMatcher));
        Assert.Throws<ArgumentException>(() => new TraceStep(Guid.NewGuid(), null, TraceOperationKind.Approval, "review", Start,
            TraceStepStatus.Waiting, workOrderId: Guid.NewGuid()));
    }

    [Fact]
    public void TokenUsageReusesExistingModelAndRejectsInconsistentTotals()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TokenUsage(-1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TokenUsage(0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TokenUsage(0, 0, -1));
        Assert.Throws<ArgumentException>(() => Step(kind: TraceOperationKind.Llm, usage: new(2, 3, 4)));
        Assert.Throws<ArgumentException>(() => Step(usage: new(2, 3, 5)));
        Assert.Throws<ArgumentException>(() => Step(cost: new(1m, "USD")));
        var usage = new TokenUsage(2, 3, 5);
        Assert.Same(usage, Step(kind: TraceOperationKind.Llm, usage: usage).Usage);
    }

    [Fact]
    public void AccountingSumsDistinctAttemptsAndSeparatesMissingDataAndCurrencies()
    {
        var a = Step(kind: TraceOperationKind.Llm, status: TraceStepStatus.Completed, usage: new(2, 3, 5), cost: new(0.1m, "USD"));
        var b = Step(kind: TraceOperationKind.Llm, status: TraceStepStatus.Failed, usage: new(5, 2, 7), cost: new(0.2m, "USD"));
        var c = Step(kind: TraceOperationKind.Llm, status: TraceStepStatus.Cancelled, cost: new(1m, "EUR"));
        var unknown = Step(kind: TraceOperationKind.Llm);
        var summary = RunUsageSummary.Aggregate([a, b, c, unknown, Step()]);
        Assert.Equal(new TokenUsage(7, 5, 12), summary.KnownTokenUsage);
        Assert.Equal(2, summary.UnreportedTokenCallCount);
        Assert.Equal(1, summary.UnpricedCallCount);
        Assert.Equal(new MonetaryCost(0.3m, "USD"), summary.KnownCostsByCurrency.Single(c => c.Currency == "USD"));
        Assert.Equal(new MonetaryCost(1m, "EUR"), summary.KnownCostsByCurrency.Single(c => c.Currency == "EUR"));
        Assert.Throws<NotSupportedException>(() => ((IList<MonetaryCost>)summary.KnownCostsByCurrency).Clear());
        Assert.Throws<ArgumentException>(() => RunUsageSummary.Aggregate([a, a]));
        Assert.Throws<ArgumentException>(() => RunUsageSummary.Aggregate([null!]));
        Assert.Throws<ArgumentNullException>(() => RunUsageSummary.Aggregate(null!));
    }

    [Fact]
    public void UnknownCostIsNotZeroAndOverflowNeverSilentlyWraps()
    {
        var unknown = RunUsageSummary.Aggregate([Step(kind: TraceOperationKind.Llm)]);
        Assert.Empty(unknown.KnownCostsByCurrency);
        Assert.Equal(1, unknown.UnpricedCallCount);
        var free = RunUsageSummary.Aggregate([Step(kind: TraceOperationKind.Llm, cost: new(0m, "USD"))]);
        Assert.Equal(0m, Assert.Single(free.KnownCostsByCurrency).Amount);
        Assert.Equal(0, free.UnpricedCallCount);
        Assert.Throws<OverflowException>(() => RunUsageSummary.Aggregate([
            Step(kind: TraceOperationKind.Llm, usage: new(int.MaxValue, 0, int.MaxValue)),
            Step(kind: TraceOperationKind.Llm, usage: new(1, 0, 1))]));
        Assert.Throws<OverflowException>(() => new MonetaryCost(decimal.MaxValue, "USD").Add(new(1m, "USD")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("usd")]
    [InlineData("US")]
    [InlineData("123")]
    public void MoneyRequiresExplicitCurrency(string currency) =>
        Assert.Throws<ArgumentException>(() => new MonetaryCost(1m, currency));

    [Fact]
    public void MoneyRejectsNegativeAndMixedCurrencySums()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonetaryCost(-0.01m, "USD"));
        Assert.Throws<ArgumentNullException>(() => new MonetaryCost(1m, null!));
        Assert.Throws<ArgumentException>(() => new MonetaryCost(1m, "USD").Add(new(1m, "EUR")));
    }

    [Fact]
    public void StoreSurfaceIsCancellableAndNeutralAndTraceTypesHaveNoAuthorityMethods()
    {
        foreach (var method in typeof(IRunTraceStore).GetMethods())
        {
            Assert.Equal(typeof(CancellationToken), method.GetParameters().Last().ParameterType);
            Assert.Equal(typeof(Task<>), method.ReturnType.GetGenericTypeDefinition());
        }
        // A narrow public-surface guard against adding approval/tool execution capabilities to observations.
        var publicMethods = typeof(TraceStep).GetMethods().Concat(typeof(RunTraceSnapshot).GetMethods());
        Assert.DoesNotContain(publicMethods, m => m.Name is "Approve" or "Reject" or "Dispatch" or "ExecuteAsync" or "RequestCancellation");
        var references = typeof(IRunTraceStore).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, a => a.Name!.StartsWith("IndustrialCopilot.") && a.Name != "IndustrialCopilot.Domain");
        Assert.DoesNotContain(references, a => a.Name!.Contains("EntityFramework") || a.Name.Contains("AspNetCore") || a.Name.Contains("OpenTelemetry"));
    }
}
