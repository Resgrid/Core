using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Repositories.DataRepository.Contexts;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Repositories.DataRepository
{
	public class GenericDataRepository<T> : RepositoryBase<T>, IGenericDataRepository<T> where T : class, IEntity
	{
        public GenericDataRepository(DataContext context, IISolationLevel isolationLevel)
            : base(context, isolationLevel) { }
	}
}