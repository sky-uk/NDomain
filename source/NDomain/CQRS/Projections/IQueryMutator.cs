using System;

namespace NDomain.CQRS.Projections
{
    public interface IQueryMutator<TProjection>
        where TProjection : new()
    {
        Func<TProjection, IAggregateEvent, TProjection> GetEventHandler(IAggregateEvent ev);

        bool TryGetEventHandler(IAggregateEvent ev, out Func<TProjection, IAggregateEvent, TProjection> handler);
    }
}