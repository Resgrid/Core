using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Udf
{
	public class SelectActiveUdfDefinitionByDeptAndTypeQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;

		public SelectActiveUdfDefinitionByDeptAndTypeQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectActiveUdfDefinitionByDeptAndTypeQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.UdfDefinitionsTableName,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%DID%", "%ENTITYTYPE%" },
					new string[] { "DepartmentId", "EntityType" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}

