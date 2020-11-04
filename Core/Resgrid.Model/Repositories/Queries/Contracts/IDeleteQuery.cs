namespace Resgrid.Model.Repositories.Queries.Contracts
{
	public interface IDeleteQuery : IQuery
	{
		string GetQuery();
		string GetQuery<TEntity>(TEntity entity) where TEntity : class, IEntity;
	}
}
