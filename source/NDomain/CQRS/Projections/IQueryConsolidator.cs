using System.Threading.Tasks;

namespace NDomain.CQRS.Projections
{
    public interface IQueryConsolidator<TProjection>
        where TProjection : new()
    {
        Task Consolidate(string aggregateId);
    }
}