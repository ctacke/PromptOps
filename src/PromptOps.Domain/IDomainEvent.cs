namespace PromptOps.Domain;

/// <summary>
/// Marker for something an aggregate wants observed after it's persisted. Deliberately framework-free
/// (ADR-0002) — <c>Application</c> wraps these for dispatch via whatever mechanism it chooses (ADR-0008: MediatR).
/// </summary>
public interface IDomainEvent;
