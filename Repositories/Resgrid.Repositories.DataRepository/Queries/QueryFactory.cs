using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Repositories.Queries.Contracts;
using System;
using System.Collections.Concurrent;
using Resgrid.Model;

namespace Resgrid.Repositories.DataRepository.Queries
{
	public class QueryFactory : IQueryFactory
	{
		private readonly ConcurrentDictionary<Type, IQuery> _queryList;
		public QueryFactory(IQueryList queryList)
		{
			_queryList = queryList.RetrieveQueryList();
		}

		public string GetDeleteQuery<TQuery>() where TQuery : IDeleteQuery
		{
			try
			{
				var query = _queryList[typeof(TQuery)] as IDeleteQuery;
				return (query).GetQuery();
			}
			catch (Exception)
			{
				throw new NotImplementedException($"Query {typeof(TQuery)} not found.");
			}
		}

		public string GetDeleteQuery<TQuery, TEntity>(TEntity entity) where TQuery : IDeleteQuery where TEntity : class, IEntity
		{
			try
			{
				var query = _queryList[typeof(TQuery)] as IDeleteQuery;
				return (query).GetQuery(entity);
			}
			catch (Exception)
			{
				throw new NotImplementedException($"Query {typeof(TQuery)} not found.");
			}
		}

		public string GetInsertQuery<TQuery, TEntity>(TEntity entity) where TQuery : IInsertQuery where TEntity : class, IEntity
		{
			try
			{
				var query = _queryList[typeof(TQuery)] as IInsertQuery;
				return (query).GetQuery(entity);
			}
			catch (Exception)
			{
				throw new NotImplementedException($"Query {typeof(TQuery)} not found.");
			}
		}

		public string GetQuery<TQuery>() where TQuery : ISelectQuery
		{
			try
			{
				var query = _queryList[typeof(TQuery)] as ISelectQuery;
				return (query).GetQuery();
			}
			catch (Exception)
			{
				throw new NotImplementedException($"Query {typeof(TQuery)} not found.");
			}
		}

		public string GetQuery<TQuery, TEntity>() where TQuery : ISelectQuery where TEntity : class, IEntity
		{
			try
			{
				var query = _queryList[typeof(TQuery)] as ISelectQuery;
				return (query).GetQuery<TEntity>();
			}
			catch (Exception)
			{
				throw new NotImplementedException($"Query {typeof(TQuery)} not found.");
			}
		}

		public string GetUpdateQuery<TQuery, TEntity>(TEntity entity) where TQuery : IUpdateQuery where TEntity : class, IEntity
		{
			try
			{
				var query = _queryList[typeof(TQuery)] as IUpdateQuery;
				return (query).GetQuery(entity);
			}
			catch (Exception)
			{
				throw new NotImplementedException($"Query {typeof(TQuery)} not found.");
			}
		}
	}
}
