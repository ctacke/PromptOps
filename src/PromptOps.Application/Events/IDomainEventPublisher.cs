using PromptOps.Domain;

namespace PromptOps.Application.Events;

/// <summary>
/// Dispatches a domain event to every registered <see cref="IDomainEventHandler{TEvent}"/> for its
/// concrete type. Hand-rolled rather than a third-party mediator (see ADR-0008 in architecture.md
/// for why) — the need is small (one publisher, a handful of DI-resolved handlers) and this avoids
/// taking on an external dependency for it.
/// </summary>
public interface IDomainEventPublisher
{
    Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}
