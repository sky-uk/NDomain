using System;
using System.Threading.Tasks;

namespace NDomain.CQRS.Projections
{
    /// <summary>
    /// [Experimental] Provides support for projections from aggregate events into eventually-consistent read models.
    /// The idea is that the read models can be disposed of, and be rebuilt from the events projection, making it suitable for LRU caches.
    /// Ensures that events that update read models are processed according to its sequence numbers within the event stream.
    /// Concurrent updates are not an issue, because everytime a new event updates the read model, the source projection events are checked to verify if the read model's version is behind and in that case, missed events will be reprocessed.
    /// </summary>
    /// <remarks>This is still experimental work and most likely will undergo changes in newer versions.</remarks>
    /// <typeparam name="T"></typeparam>
    public class QueryEventsHandler<T> : IQueryEventHandler<T>, IQueryConsolidator<T>
        where T : new()
    {
        private readonly IQueryStore<T> queryStore;
        private readonly IEventStore eventStore;
        private readonly IQueryMutator<T> mutator;

        public QueryEventsHandler(IQueryStore<T> queryStore, IEventStore eventStore, IQueryMutator<T> mutator)
        {
            this.queryStore = queryStore;
            this.eventStore = eventStore;
            this.mutator = mutator;
        }

        public async Task OnEvent(IAggregateEvent ev, string queryId = null)
        {
            queryId = queryId ?? ev.AggregateId;
            var query = await queryStore.Get(queryId);

            var data = query.Data;

            if (query.Version >= ev.SequenceId)
            {
                // event already applied
                return;
            }

            int originalVersion = query.Version;
            if (originalVersion < ev.SequenceId - 1)
            {
                // fastforward to the current event's version
                query = await FastForward(query, ev.SequenceId);
            }
            else
            {
                // query is sync, just apply the event
                var eventHandler = mutator.GetEventHandler(ev);
                query.Data = eventHandler(data, ev);
                query.Version = ev.SequenceId;
                query.DateUtc = DateTime.UtcNow;
            }

            await queryStore.Set(queryId, query, originalVersion);
        }

        public async Task Consolidate(string aggregateId)
        {
            var query = await queryStore.Get(aggregateId);
            var originalVersion = query.Version;
            var updatedQuery = await FastForward(query, int.MaxValue);

            if (updatedQuery.Version <= query.Version)
            {
                //Nothing to do as we're already at the latest version
                return;
            }

            await queryStore.Set(query.Id, updatedQuery, originalVersion);
        }

        private async Task<Query<T>> FastForward(Query<T> query, int maxVersion)
        {
            var start = query.Version + 1;
            var events = await eventStore
                .LoadRangeWithoutCheckingUncommitted(query.Id, start, maxVersion);

            var data = query.Data;
            int lastVersion = 0;

            foreach (var @event in events)
            {
                var evHandler = mutator.GetEventHandler(@event);
                data = evHandler(data, @event);
                lastVersion = @event.SequenceId;
            }

            return new Query<T>
            {
                Data = data,
                DateUtc = DateTime.UtcNow,
                Id = query.Id,
                Version = lastVersion
            };
        }
    }
}
