using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NDomain
{
    public interface IAggregateListRepository
    {
        Task<IEnumerable<string>> GetAggregates<TAggregate>()
            where TAggregate : IAggregate;

        Task<IEnumerable<string>> GetAggregates(Type aggregateType);

        Task<IEnumerable<string>> GetAggregates(string aggregateKey);
    }
}