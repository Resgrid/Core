using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Udf
{
	public class DeleteUdfFieldValuesByEntityAndDefinitionQuery : IDeleteQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;

		public DeleteUdfFieldValuesByEntityAndDefinitionQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.DeleteUdfFieldValuesByEntityAndDefinitionQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.UdfFieldValuesTableName,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%ENTITYTYPE%", "%ENTITYID%", "%DEFINITIONID%" },
					new string[] { "EntityType", "EntityId", "UdfDefinitionId" });

			return query;
		}

		public string GetQuery<TEntity>(TEntity entity) where TEntity : class, IEntity
		{
			return GetQuery();
		}
	}
}

