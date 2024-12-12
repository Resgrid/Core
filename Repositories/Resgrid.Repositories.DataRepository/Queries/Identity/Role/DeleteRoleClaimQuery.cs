using System;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Identity.Role
{
	public class DeleteRoleClaimQuery : IDeleteQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public DeleteRoleClaimQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.DeleteRoleClaimQuery
										 .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
																 _sqlConfiguration.RoleClaimTable,
																 _sqlConfiguration.ParameterNotation,
																 new string[]
																 {
																	 "%ROLEID%",
																	 "%CLAIMVALUE%",
																	 "%CLAIMTYPE%"
																 },
																 new string[]
																 {
																	 "RoleId",
																	 "ClaimValue",
																	 "ClaimType"
																 });

			return query;
		}

		public string GetQuery<TEntity>(TEntity entity) where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
