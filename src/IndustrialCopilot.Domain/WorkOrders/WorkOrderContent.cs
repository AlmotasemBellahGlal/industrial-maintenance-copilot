namespace IndustrialCopilot.Domain.WorkOrders;

public sealed record WorkOrderContent
{
    public Guid EquipmentId { get; }
    public Guid ManualId { get; }
    public Guid ManualRevisionId { get; }
    public string ReportedSymptom { get; }
    public string Description { get; }
    public IReadOnlyList<WorkOrderAction> Actions { get; }

    public WorkOrderContent(Guid equipmentId, Guid manualId, Guid manualRevisionId,
        string reportedSymptom, string description, IEnumerable<WorkOrderAction> actions)
    {
        if (equipmentId == Guid.Empty) throw new ArgumentException("Equipment identity is required.", nameof(equipmentId));
        if (manualId == Guid.Empty) throw new ArgumentException("Manual identity is required.", nameof(manualId));
        if (manualRevisionId == Guid.Empty) throw new ArgumentException("Manual revision identity is required.", nameof(manualRevisionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(reportedSymptom);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(actions);
        var snapshot = actions.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(action => action is null))
            throw new ArgumentException("Nonempty actions with no null entries are required.", nameof(actions));
        for (var index = 1; index < snapshot.Length; index++)
            if (snapshot[index].Order <= snapshot[index - 1].Order)
                throw new ArgumentException("Action orders must be strictly increasing.", nameof(actions));
        EquipmentId = equipmentId;
        ManualId = manualId;
        ManualRevisionId = manualRevisionId;
        ReportedSymptom = reportedSymptom;
        Description = description;
        Actions = Array.AsReadOnly(snapshot);
    }

    public bool Equals(WorkOrderContent? other) =>
        other is not null && EquipmentId == other.EquipmentId && ManualId == other.ManualId
        && ManualRevisionId == other.ManualRevisionId && ReportedSymptom == other.ReportedSymptom
        && Description == other.Description && Actions.SequenceEqual(other.Actions);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(EquipmentId);
        hash.Add(ManualId);
        hash.Add(ManualRevisionId);
        hash.Add(ReportedSymptom);
        hash.Add(Description);
        foreach (var action in Actions) hash.Add(action);
        return hash.ToHashCode();
    }
}
