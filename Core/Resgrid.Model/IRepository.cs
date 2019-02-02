using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Resgrid.Model
{
	public interface IRepository<T> where T : class
	{
		void DeleteOnSubmit(T entity);
		void DeleteOnSubmitAsync(T entity, CancellationToken cancellationToken = default(CancellationToken));
		IQueryable<T> GetAll();
		//T GetById(object id);
		void SaveOrUpdate(T entity);
		void SaveOrUpdateAsync(T entity, CancellationToken cancellationToken = default(CancellationToken));
		void DeleteAll(IEnumerable<T> entites);
		void DeleteAllAsync(IEnumerable<T> entitesToDelete, CancellationToken cancellationToken = default(CancellationToken));
		void SaveOrUpdateAll(IEnumerable<T> entitiesToAdd);
		void BulkInsert(IEnumerable<T> entitiesToAdd);
	}
}
