using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Udf
{
	public class UpdateUdfDefinitionsToInactiveQuery : IUpdateQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;

		public UpdateUdfDefinitionsToInactiveQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		string IUpdateQuery.GetQuery<TEntity>(TEntity entity)
		{
			var query = _sqlConfiguration.UpdateUdfDefinitionsToInactiveQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.UdfDefinitionsTableName,
					_sqlConfiguration.ParameterNotation,
					new[] { "%DID%", "%ENTITYTYPE%" },
					new[] { "DepartmentId", "EntityType" });

			return query;
		}
	}
}


