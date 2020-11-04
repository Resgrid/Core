using Resgrid.Model.Repositories.Queries.Contracts;

namespace Resgrid.Model.Repositories.Queries
{
	public interface IQueryFactory
	{
		string GetQuery<TQuery>() where TQuery : ISelectQuery;
		string GetQuery<TQuery, TEntity>() where TQuery : ISelectQuery where TEntity : class, IEntity;

		string GetInsertQuery<TQuery, TEntity>(TEntity entity) where TQuery : IInsertQuery where TEntity : class, IEntity;

		string GetUpdateQuery<TQuery, TEntity>(TEntity entity) where TQuery : IUpdateQuery where TEntity : class, IEntity;
		string GetDeleteQuery<TQuery>() where TQuery : IDeleteQuery;
		string GetDeleteQuery<TQuery, TEntity>(TEntity entity) where TQuery : IDeleteQuery where TEntity : class, IEntity;
	}
}
