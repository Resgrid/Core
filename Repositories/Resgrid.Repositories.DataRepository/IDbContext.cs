using System.Data.Entity;

namespace Resgrid.Repositories.DataRepository
{
	public interface IDbContext
	{
		IDbSet<TEntity> Set<TEntity>() where TEntity : class;
		int SaveChanges();

	    bool IsSqlCe();
	}
}