using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Udf
{
	public class SelectUdfFieldsByDefinitionIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;

		public SelectUdfFieldsByDefinitionIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectUdfFieldsByDefinitionIdQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.UdfFieldsTableName,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%DEFINITIONID%" },
					new string[] { "UdfDefinitionId" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			return GetQuery();
		}
	}
}

