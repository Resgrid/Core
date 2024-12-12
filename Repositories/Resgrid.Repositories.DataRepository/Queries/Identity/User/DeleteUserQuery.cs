using System;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Identity.User
{
	public class DeleteUserQuery : IDeleteQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public DeleteUserQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.DeleteUserQuery
										 .ReplaceDeleteQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
																	   _sqlConfiguration.UserTable,
																	   "Id", // TODO: Fix MEEEE!
																	   $"{_sqlConfiguration.ParameterNotation}Id");

			return query;
		}

		public string GetQuery<TEntity>(TEntity entity) where TEntity : class, IEntity
		{
			throw new NotImplementedException();
		}
	}
}
