using NDomain.Helpers;
using System;
using System.Collections.Generic;
using NDomain.EventSourcing;
using Newtonsoft.Json;

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

        public  TProjection InvokeEventMutator(TProjection currentProjection, IAggregateEvent @event)
        {
            var handler = GetEventHandler(@event.Name);
            return handler(currentProjection, @event);
        }

        private Func<TProjection, IAggregateEvent, TProjection> GetEventHandler(string eventName)
        {
            if (!_handlers.ContainsKey(eventName))
            {
                throw new ArgumentException($"The type {GetType().Name} does not contain a handler to the event {eventName}");
            }

            return _handlers[eventName];
        }
    }
}
