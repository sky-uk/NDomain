using System;

namespace NDomain.CQRS.Projections
{
    public interface IQueryMutator<TProjection>
        where TProjection : new()
    {
        TProjection InvokeEventMutator(TProjection currentProjection, IAggregateEvent @event);
    }
}