using NDomain.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NDomain.EventSourcing
{
    public class EventStore : IEventStore
    {
        readonly IEventStoreDb db;
        readonly IEventStoreBus bus;
        readonly IEventStoreSerializer serializer;
        readonly ILogger logger;

        public EventStore(IEventStoreDb db, 
                          IEventStoreBus bus,
                          IEventStoreSerializer serializer,
                          ILoggerFactory loggerFactory)
        {
            this.db = db;
            this.bus = bus;
            this.serializer = serializer;
            this.logger = loggerFactory.GetLogger(typeof(EventStore));
        }

        public async Task<IEnumerable<IAggregateEvent>> Load(string aggregateId)
        {
            var transaction = DomainTransaction.Current;
            if (transaction != null && transaction.DeliveryCount > 1)
            {
                await CheckAndProcessUncommittedEvents(aggregateId, transaction.Id);
            }

            var sourceEvents = await this.db.Load(aggregateId);

            var events = sourceEvents.Select(e => this.serializer.Deserialize(e));
            return events.ToArray();
        }

        public async Task<IEnumerable<IAggregateEvent>> LoadRange(string aggregateId, int start, int end)
        {
            var transaction = DomainTransaction.Current;
            if (transaction != null && transaction.DeliveryCount > 1)
            {
                await CheckAndProcessUncommittedEvents(aggregateId, transaction.Id);
            }

            var sourceEvents = await this.db.LoadRange(aggregateId, start, end);

            var events = sourceEvents.Select(e => this.serializer.Deserialize(e));
            return events.ToArray();
        }

        // This should only be used by FastForward on projection handling
        public async Task<IEnumerable<IAggregateEvent>> LoadRangeWithoutCheckingUncommitted(string aggregateId, int start, int end)
        {
            var sourceEvents = await this.db.LoadRange(aggregateId, start, end);

            var events = sourceEvents.Select(e => this.serializer.Deserialize(e));
            return events.ToArray();
        }

        public async Task Append(string aggregateId, int expectedVersion, IEnumerable<IAggregateEvent> events)
        {
            var sourceEvents = events.Select(e => this.serializer.Serialize(e))
                                     .ToArray();

            var transaction = DomainTransaction.Current;
            var transactionId = transaction != null ? transaction.Id : Guid.NewGuid().ToString();

            await this.db.Append(aggregateId, transactionId, expectedVersion, sourceEvents);
            this.logger.Info($"Event store events appended to stream [correlationId:{transaction?.CorrelationId}] [transactionId:{transactionId}] [aggregateId:{aggregateId}] [numEvents:{events.Count()}] [expectedVersion:{expectedVersion}]");
            await this.bus.Publish(sourceEvents);
            await this.db.Commit(aggregateId, transactionId);
            this.logger.Info($@"Event store committed transaction [correlationId:{transaction?.CorrelationId}] [transactionId:{transactionId}] [aggregateId:{aggregateId}] [numEvents:{events.Count()}] [expectedVersion:{expectedVersion}]");
        }

        private async Task CheckAndProcessUncommittedEvents(string aggregateId, string transactionId)
        {
            var uncommittedEvents = await this.db.LoadUncommitted(aggregateId, transactionId);
            if (uncommittedEvents.Any())
            {
                await this.bus.Publish(uncommittedEvents);
                await this.db.Commit(aggregateId, transactionId);
            }
        }
    }
}
