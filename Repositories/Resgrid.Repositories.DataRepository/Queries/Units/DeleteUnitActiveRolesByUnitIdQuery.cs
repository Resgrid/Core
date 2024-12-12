using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;
using System;

namespace Resgrid.Repositories.DataRepository.Queries.Units
{
	public class DeleteUnitActiveRolesByUnitIdQuery : IDeleteQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public DeleteUnitActiveRolesByUnitIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.DeleteUnitActiveRolesByUnitIdQuery
				.ReplaceDeleteQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.UnitActiveRolesTable,
					"UnitId",
					"%UNITID%");

			return query;
		}

		public string GetQuery<TEntity>(TEntity entity) where TEntity : class, IEntity
		{
			throw new NotImplementedException();
		}
	}
}
