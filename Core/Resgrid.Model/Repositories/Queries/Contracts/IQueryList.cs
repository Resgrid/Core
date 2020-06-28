using System;
using System.Collections.Concurrent;

namespace Resgrid.Model.Repositories.Queries.Contracts
{
    public interface IQueryList
    {
        ConcurrentDictionary<Type, IQuery> RetrieveQueryList();
    }
}
