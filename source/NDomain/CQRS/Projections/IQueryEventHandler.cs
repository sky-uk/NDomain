using System.Threading.Tasks;

namespace NDomain.CQRS.Projections
{
    public interface IQueryEventHandler<TProjection>
        where TProjection : new()
    {
        Task OnEvent(IAggregateEvent message, string queryId = null);
    }
}