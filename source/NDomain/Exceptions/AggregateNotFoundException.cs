using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NDomain.Exceptions
{
    public class AggregateNotFoundException : Exception
    {
        public AggregateNotFoundException(string id)
            : base($"aggregate not found [id:{id}]")
        {           
        }
    }
}
