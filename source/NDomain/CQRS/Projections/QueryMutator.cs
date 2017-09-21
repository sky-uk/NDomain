using NDomain.Helpers;
using System;
using System.Collections.Generic;

namespace NDomain.CQRS.Projections
{
    public abstract class QueryMutator<TProjection> : IQueryMutator<TProjection>
        where TProjection : new()
    {
        private readonly IDictionary<string, Func<TProjection, IAggregateEvent, TProjection>> _handlers;

        protected QueryMutator()
        {
            _handlers = ReflectionUtils.FindQueryEventHandlerMethods<TProjection>(this);
        }
        public virtual Func<TProjection, IAggregateEvent, TProjection> GetEventHandler(IAggregateEvent ev)
        {
            if (!_handlers.ContainsKey(ev.Name))
            {
                throw new ArgumentException($"The type {this.GetType().Name} does not contain a handler to the event {ev.Name}");
            }

            return _handlers[ev.Name];
        }

        public virtual bool TryGetEventHandler(IAggregateEvent ev, out Func<TProjection, IAggregateEvent, TProjection> handler)
        {
            return _handlers.TryGetValue(ev.Name, out handler);
        }
    }
}
