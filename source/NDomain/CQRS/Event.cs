using System;

namespace NDomain.CQRS
{
    public class Event<T> : IEvent<T>
    {
        private readonly DateTime dateUtc;
        private readonly string name;
        private readonly int sequenceId;
        private readonly T payload;

        public Event(DateTime dateUtc, T payload)
            : this(dateUtc, typeof(T).Name, 0, payload)
        {
        }

        public Event(DateTime dateUtc, int sequenceId, T payload)
            : this(dateUtc, typeof(T).Name, sequenceId, payload)
        {
        }

        public Event(DateTime dateUtc, string name, int sequenceId, T payload)
            
        {
            this.dateUtc = dateUtc;
            this.name = name;
            this.sequenceId = sequenceId;
            this.payload = payload;
        }

        public DateTime DateUtc { get { return dateUtc; } }
        public string Name { get { return name; } }
        public int SequenceId { get { return sequenceId; } }
        public T Payload { get { return payload; } }

        object IEvent.Payload
        {
            get { return this.payload; }
        }
    }

}
