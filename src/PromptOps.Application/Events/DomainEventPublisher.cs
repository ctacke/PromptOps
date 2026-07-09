using Microsoft.Extensions.DependencyInjection;
using PromptOps.Domain;

namespace PromptOps.Application.Events;

/// <summary>Resolves <see cref="IDomainEventHandler{TEvent}"/> instances for the event's runtime type via DI and invokes each in turn.</summary>
public sealed class DomainEventPublisher(IServiceProvider serviceProvider) : IDomainEventPublisher
{
    public async Task PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
        var handleMethod = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))
            ?? throw new MissingMethodException(handlerType.FullName, nameof(IDomainEventHandler<IDomainEvent>.HandleAsync));

        var handlers = (IEnumerable<object>)serviceProvider.GetServices(handlerType)!;

        foreach (var handler in handlers)
        {
            var task = (Task)handleMethod.Invoke(handler, [domainEvent, cancellationToken])!;
            await task;
        }
    }
}
