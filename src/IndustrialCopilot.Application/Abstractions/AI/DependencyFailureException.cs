namespace IndustrialCopilot.Application.Abstractions.AI;

public enum DependencyFailureKind { Terminal, Transient, Timeout }
/// <summary>Sanitized classification, not provider payloads. Unknown failures are terminal by default.</summary>
public class DependencyFailureException(DependencyFailureKind failure) : Exception("Dependency operation could not complete.")
{
    public DependencyFailureKind Failure { get; } = failure;
}
