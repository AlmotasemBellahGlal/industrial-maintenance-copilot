namespace IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher;

public sealed record EquipmentManualCandidate
{
    public Guid EquipmentId { get; }
    public string EquipmentName { get; }
    public Guid DocumentId { get; }
    public Guid ManualRevisionId { get; }

    public EquipmentManualCandidate(Guid equipmentId, string equipmentName, Guid documentId, Guid manualRevisionId)
    {
        if (equipmentId == Guid.Empty) throw new ArgumentException("Equipment identity is required.", nameof(equipmentId));
        if (documentId == Guid.Empty) throw new ArgumentException("Document identity is required.", nameof(documentId));
        if (manualRevisionId == Guid.Empty) throw new ArgumentException("Revision identity is required.", nameof(manualRevisionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(equipmentName);
        EquipmentId = equipmentId;
        EquipmentName = equipmentName;
        DocumentId = documentId;
        ManualRevisionId = manualRevisionId;
    }
}
