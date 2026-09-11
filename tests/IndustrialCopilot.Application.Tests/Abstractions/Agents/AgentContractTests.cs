using IndustrialCopilot.Application.Abstractions.Agents;
using IndustrialCopilot.Application.Abstractions.Agents.Evidence;
using IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher;
using IndustrialCopilot.Application.Abstractions.Agents.DiagnosticPlanning;
using IndustrialCopilot.Application.Abstractions.Agents.WorkOrders;

namespace IndustrialCopilot.Application.Tests.Abstractions.Agents;

public class AgentContractTests
{
    private static EquipmentManualCandidate Candidate() => new(Guid.NewGuid(), "Pump", Guid.NewGuid(), Guid.NewGuid());
    private static GroundedEvidence Evidence(EquipmentManualCandidate c) => new(c.DocumentId, c.ManualRevisionId, Guid.NewGuid(), "Page 3", "Inspect coupling");
    private static SymptomMatch Match(EquipmentManualCandidate c) => new(c, [new MatchedSymptom("Vibration", [Evidence(c)])]);
    private static DiagnosticPlan Plan(EquipmentManualCandidate c) => new(c, [new ProposedDiagnosticStep(1, "Inspect", [Evidence(c)])], []);
    private static WorkOrderProposal Proposal(EquipmentManualCandidate c) => new(c, "Vibration", "Repair coupling",
        [new ProposedWorkOrderAction(1, "Replace coupling", [Evidence(c)])], []);

    [Fact]
    public void EvidenceAndCandidateRejectEmptyIdentities()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new GroundedEvidence(Guid.Empty, id, id, "Page 1", "Text"));
        Assert.Throws<ArgumentException>(() => new GroundedEvidence(id, Guid.Empty, id, "Page 1", "Text"));
        Assert.Throws<ArgumentException>(() => new GroundedEvidence(id, id, Guid.Empty, "Page 1", "Text"));
        Assert.Throws<ArgumentException>(() => new EquipmentManualCandidate(Guid.Empty, "Pump", id, id));
        Assert.Throws<ArgumentException>(() => new EquipmentManualCandidate(id, "Pump", Guid.Empty, id));
        Assert.Throws<ArgumentException>(() => new EquipmentManualCandidate(id, "Pump", id, Guid.Empty));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RequiredTextCannotBeMissing(string? text)
    {
        var c = Candidate();
        var e = Evidence(c);
        Assert.ThrowsAny<ArgumentException>(() => new GroundedEvidence(c.DocumentId, c.ManualRevisionId, e.ChunkId, text!, "Text"));
        Assert.ThrowsAny<ArgumentException>(() => new GroundedEvidence(c.DocumentId, c.ManualRevisionId, e.ChunkId, "Page 1", text!));
        Assert.ThrowsAny<ArgumentException>(() => new EquipmentManualCandidate(c.EquipmentId, text!, c.DocumentId, c.ManualRevisionId));
        Assert.ThrowsAny<ArgumentException>(() => new MatchedSymptom(text!, [e]));
        Assert.ThrowsAny<ArgumentException>(() => new ProposedDiagnosticStep(1, text!, [e]));
        Assert.ThrowsAny<ArgumentException>(() => new ProposedWorkOrderAction(1, text!, [e]));
        Assert.ThrowsAny<ArgumentException>(() => new ProposedSafetyPrerequisite(text!, [e]));
        Assert.ThrowsAny<ArgumentException>(() => new SymptomMatchInput(text!, [c], []));
        Assert.ThrowsAny<ArgumentException>(() => new DiagnosticPlanInput(text!, Match(c)));
        Assert.ThrowsAny<ArgumentException>(() => new WorkOrderGenerationInput(text!, Plan(c)));
        Assert.ThrowsAny<ArgumentException>(() => new WorkOrderProposal(c, "Vibration", text!, [new ProposedWorkOrderAction(1, "Repair", [e])], []));
        Assert.ThrowsAny<ArgumentException>(() => new WorkOrderProposal(c, text!, "Repair", [new ProposedWorkOrderAction(1, "Repair", [e])], []));
    }

    [Fact]
    public void EachGroundedItemRejectsNullEmptyAndDuplicateEvidence()
    {
        var e = Evidence(Candidate());
        var duplicate = new GroundedEvidence(e.DocumentId, e.ManualRevisionId, e.ChunkId, "Different locator", "Different snippet");
        Action<IReadOnlyList<GroundedEvidence>>[] constructors =
        [
            evidence => new MatchedSymptom("Vibration", evidence),
            evidence => new ProposedDiagnosticStep(1, "Inspect", evidence),
            evidence => new ProposedWorkOrderAction(1, "Repair", evidence),
            evidence => new ProposedSafetyPrerequisite("Isolate", evidence)
        ];
        foreach (var construct in constructors)
        {
            Assert.Throws<ArgumentNullException>(() => construct(null!));
            Assert.Throws<ArgumentException>(() => construct([]));
            Assert.Throws<ArgumentException>(() => construct([null!]));
            Assert.Throws<ArgumentException>(() => construct([e, duplicate]));
        }
    }

    [Fact]
    public void MatchingRequiresCandidatesAndGroundedSymptoms()
    {
        var c = Candidate();
        Assert.Throws<ArgumentNullException>(() => new SymptomMatchInput("Vibration", null!, []));
        Assert.Throws<ArgumentNullException>(() => new SymptomMatchInput("Vibration", [c], null!));
        Assert.Throws<ArgumentException>(() => new SymptomMatchInput("Vibration", [], []));
        Assert.Throws<ArgumentException>(() => new SymptomMatchInput("Vibration", [null!], []));
        var duplicate = new EquipmentManualCandidate(c.EquipmentId, "Another label", c.DocumentId, c.ManualRevisionId);
        Assert.Throws<ArgumentException>(() => new SymptomMatchInput("Vibration", [c, duplicate], []));
        var e = Evidence(c);
        Assert.Throws<ArgumentException>(() => new SymptomMatchInput("Vibration", [c], [e, e]));
        Assert.Throws<ArgumentException>(() => new SymptomMatchInput("Vibration", [c], [null!]));
        Assert.Throws<ArgumentNullException>(() => new SymptomMatch(null!, []));
        Assert.Throws<ArgumentNullException>(() => new SymptomMatch(c, null!));
        Assert.Throws<ArgumentException>(() => new SymptomMatch(c, []));
        Assert.Throws<ArgumentException>(() => new SymptomMatch(c, [null!]));
        Assert.Throws<ArgumentException>(() => new SymptomMatch(c, [new MatchedSymptom("Vibration", [e])], " "));
        _ = new SymptomMatchInput("Vibration", [c], []);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EvidenceMustMatchDocumentAndRevision(bool otherDocument)
    {
        var c = Candidate();
        var e = new GroundedEvidence(otherDocument ? Guid.NewGuid() : c.DocumentId,
            otherDocument ? c.ManualRevisionId : Guid.NewGuid(), Guid.NewGuid(), "Page 1", "Text");
        Assert.Throws<ArgumentException>(() => new SymptomMatchInput("Vibration", [c], [e]));
        Assert.Throws<ArgumentException>(() => new SymptomMatch(c, [new MatchedSymptom("Vibration", [e])]));
        Assert.Throws<ArgumentException>(() => new DiagnosticPlan(c, [new ProposedDiagnosticStep(1, "Inspect", [e])], []));
        Assert.Throws<ArgumentException>(() => new WorkOrderProposal(c, "Vibration", "Repair", [new ProposedWorkOrderAction(1, "Repair", [e])], []));
        var prerequisite = new ProposedSafetyPrerequisite("Isolate", [e]);
        Assert.Throws<ArgumentException>(() => new DiagnosticPlan(c, Plan(c).Steps, [prerequisite]));
        Assert.Throws<ArgumentException>(() => new WorkOrderProposal(c, "Vibration", "Repair", Proposal(c).Actions, [prerequisite]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void IndividualOrdersMustBePositive(int order)
    {
        var e = Evidence(Candidate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProposedDiagnosticStep(order, "Inspect", [e]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProposedWorkOrderAction(order, "Repair", [e]));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void PlansAndProposalsRejectDuplicateOrDescendingOrders(int first, int second)
    {
        var c = Candidate();
        var e = Evidence(c);
        Assert.Throws<ArgumentException>(() => new DiagnosticPlan(c,
            [new ProposedDiagnosticStep(first, "Inspect", [e]), new ProposedDiagnosticStep(second, "Measure", [e])], []));
        Assert.Throws<ArgumentException>(() => new WorkOrderProposal(c, "Vibration", "Repair",
            [new ProposedWorkOrderAction(first, "Repair", [e]), new ProposedWorkOrderAction(second, "Replace", [e])], []));
    }

    [Fact]
    public void PlansAndProposalsRequireInstructionsButAllowNoAdvisoryPrerequisites()
    {
        var c = Candidate();
        Assert.Throws<ArgumentException>(() => new DiagnosticPlan(c, [], []));
        Assert.Throws<ArgumentNullException>(() => new DiagnosticPlan(c, null!, []));
        Assert.Throws<ArgumentException>(() => new DiagnosticPlan(c, [null!], []));
        Assert.Throws<ArgumentNullException>(() => new DiagnosticPlan(null!, Plan(c).Steps, []));
        Assert.Throws<ArgumentNullException>(() => new DiagnosticPlan(c, Plan(c).Steps, null!));
        Assert.Throws<ArgumentException>(() => new DiagnosticPlan(c, Plan(c).Steps, [null!]));
        Assert.Throws<ArgumentException>(() => new WorkOrderProposal(c, "Vibration", "Repair", [], []));
        Assert.Throws<ArgumentNullException>(() => new WorkOrderProposal(c, "Vibration", "Repair", null!, []));
        Assert.Throws<ArgumentException>(() => new WorkOrderProposal(c, "Vibration", "Repair", [null!], []));
        Assert.Throws<ArgumentNullException>(() => new WorkOrderProposal(null!, "Vibration", "Repair", Proposal(c).Actions, []));
        Assert.Throws<ArgumentNullException>(() => new WorkOrderProposal(c, "Vibration", "Repair", Proposal(c).Actions, null!));
        Assert.Throws<ArgumentException>(() => new WorkOrderProposal(c, "Vibration", "Repair", Proposal(c).Actions, [null!]));
        Assert.Throws<ArgumentNullException>(() => new DiagnosticPlanInput("Vibration", null!));
        Assert.Throws<ArgumentNullException>(() => new WorkOrderGenerationInput("Vibration", null!));
        Assert.Empty(Plan(c).SafetyPrerequisites);
        Assert.Empty(Proposal(c).SafetyPrerequisites);
    }

    [Fact]
    public void ContractsOwnTheirCollectionSnapshots()
    {
        var c = Candidate();
        var e = Evidence(c);
        var evidence = new List<GroundedEvidence> { e };
        var candidates = new List<EquipmentManualCandidate> { c };
        var input = new SymptomMatchInput("Vibration", candidates, evidence);
        var symptom = new MatchedSymptom("Vibration", evidence);
        var step = new ProposedDiagnosticStep(1, "Inspect", evidence);
        var action = new ProposedWorkOrderAction(1, "Repair", evidence);
        var prerequisite = new ProposedSafetyPrerequisite("Isolate", evidence);
        var symptoms = new List<MatchedSymptom> { symptom };
        var steps = new List<ProposedDiagnosticStep> { step };
        var actions = new List<ProposedWorkOrderAction> { action };
        var prerequisites = new List<ProposedSafetyPrerequisite> { prerequisite };
        var match = new SymptomMatch(c, symptoms);
        var plan = new DiagnosticPlan(c, steps, prerequisites);
        var proposal = new WorkOrderProposal(c, "Vibration", "Repair", actions, prerequisites);
        evidence.Clear(); candidates.Clear(); symptoms.Clear(); steps.Clear(); actions.Clear(); prerequisites.Clear();
        AssertFrozen(input.Candidates); AssertFrozen(input.InitialEvidence);
        AssertFrozen(symptom.Evidence); AssertFrozen(step.Evidence);
        AssertFrozen(action.Evidence); AssertFrozen(prerequisite.Evidence);
        AssertFrozen(match.MatchedSymptoms); AssertFrozen(plan.Steps);
        AssertFrozen(plan.SafetyPrerequisites); AssertFrozen(proposal.Actions); AssertFrozen(proposal.SafetyPrerequisites);
    }

    private static void AssertFrozen<T>(IReadOnlyList<T> values)
    {
        Assert.Single(values);
        Assert.Throws<NotSupportedException>(() => ((IList<T>)values).Clear());
    }

    [Fact]
    public void SuccessRequiresPayloadAndDoesNotCarryFailureExplanation()
    {
        var c = Candidate();
        Assert.Throws<ArgumentNullException>(() => SymptomMatchResult.Success(null!));
        Assert.Throws<ArgumentNullException>(() => DiagnosticPlanResult.Success(null!));
        Assert.Throws<ArgumentNullException>(() => WorkOrderGenerationResult.Success(null!));
        var match = SymptomMatchResult.Success(Match(c));
        var plan = DiagnosticPlanResult.Success(Plan(c));
        var proposal = WorkOrderGenerationResult.Success(Proposal(c));
        Assert.Equal(AgentOutcome.Success, match.Outcome); Assert.NotNull(match.Match); Assert.Null(match.Explanation);
        Assert.Equal(AgentOutcome.Success, plan.Outcome); Assert.NotNull(plan.Plan); Assert.Null(plan.Explanation);
        Assert.Equal(AgentOutcome.Success, proposal.Outcome); Assert.NotNull(proposal.Proposal); Assert.Null(proposal.Explanation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonSuccessCarriesReasonWithoutActionablePayload(bool cannotProceed)
    {
        var match = cannotProceed ? SymptomMatchResult.CannotProceed("Conflict") : SymptomMatchResult.InsufficientEvidence("Missing evidence");
        var plan = cannotProceed ? DiagnosticPlanResult.CannotProceed("Conflict") : DiagnosticPlanResult.InsufficientEvidence("Missing evidence");
        var proposal = cannotProceed ? WorkOrderGenerationResult.CannotProceed("Conflict") : WorkOrderGenerationResult.InsufficientEvidence("Missing evidence");
        var outcome = cannotProceed ? AgentOutcome.CannotProceed : AgentOutcome.InsufficientEvidence;
        Assert.Equal(outcome, match.Outcome); Assert.Null(match.Match); Assert.False(string.IsNullOrWhiteSpace(match.Explanation));
        Assert.Equal(outcome, plan.Outcome); Assert.Null(plan.Plan); Assert.False(string.IsNullOrWhiteSpace(plan.Explanation));
        Assert.Equal(outcome, proposal.Outcome); Assert.Null(proposal.Proposal); Assert.False(string.IsNullOrWhiteSpace(proposal.Explanation));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void NonSuccessRequiresExplanation(string? reason)
    {
        Assert.ThrowsAny<ArgumentException>(() => SymptomMatchResult.InsufficientEvidence(reason!));
        Assert.ThrowsAny<ArgumentException>(() => SymptomMatchResult.CannotProceed(reason!));
        Assert.ThrowsAny<ArgumentException>(() => DiagnosticPlanResult.InsufficientEvidence(reason!));
        Assert.ThrowsAny<ArgumentException>(() => DiagnosticPlanResult.CannotProceed(reason!));
        Assert.ThrowsAny<ArgumentException>(() => WorkOrderGenerationResult.InsufficientEvidence(reason!));
        Assert.ThrowsAny<ArgumentException>(() => WorkOrderGenerationResult.CannotProceed(reason!));
    }

    [Fact]
    public void PublicApiShapePreventsContradictoryResultConstructionAndAuthorityFields()
    {
        Type[] results = [typeof(SymptomMatchResult), typeof(DiagnosticPlanResult), typeof(WorkOrderGenerationResult)];
        foreach (var type in results)
        {
            Assert.Empty(type.GetConstructors());
            Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
        }
        // Explicit allow-lists protect the advisory boundary from accidental authority-bearing additions.
        Assert.Equal(new[] { "Description", "Evidence" }, typeof(ProposedSafetyPrerequisite).GetProperties().Select(p => p.Name).OrderBy(n => n));
        Assert.Equal(new[] { "Actions", "Description", "ReportedSymptom", "SafetyPrerequisites", "SelectedCandidate" },
            typeof(WorkOrderProposal).GetProperties().Select(p => p.Name).OrderBy(n => n));
        Assert.Equal(new[] { "ChunkId", "DocumentId", "Locator", "ManualRevisionId", "Snippet" },
            typeof(GroundedEvidence).GetProperties().Select(p => p.Name).OrderBy(n => n));
    }

    [Fact]
    public void IncreasingDiagnosticStepsCanShareCitationAcrossInstructions()
    {
        var candidate = Candidate();
        var evidence = Evidence(candidate);
        var first = new ProposedDiagnosticStep(1, "Inspect coupling", [evidence]);
        var second = new ProposedDiagnosticStep(3, "Measure vibration", [evidence]);
        var plan = new DiagnosticPlan(candidate, [first, second], []);

        Assert.Equal(new[] { first, second }, plan.Steps);
        Assert.Equal(plan.Steps[0].Evidence[0], plan.Steps[1].Evidence[0]);
        Assert.Throws<ArgumentException>(() => new ProposedDiagnosticStep(1, "Inspect coupling", [evidence, evidence]));
    }

    [Fact]
    public void IncreasingWorkOrderActionsCanShareCitationAcrossActions()
    {
        var candidate = Candidate();
        var evidence = Evidence(candidate);
        var first = new ProposedWorkOrderAction(1, "Replace coupling", [evidence]);
        var second = new ProposedWorkOrderAction(3, "Align assembly", [evidence]);
        var proposal = new WorkOrderProposal(candidate, "Vibration", "Repair assembly", [first, second], []);

        Assert.Equal(new[] { first, second }, proposal.Actions);
        Assert.Equal(proposal.Actions[0].Evidence[0], proposal.Actions[1].Evidence[0]);
        Assert.Throws<ArgumentException>(() => new ProposedWorkOrderAction(1, "Replace coupling", [evidence, evidence]));
    }
}
