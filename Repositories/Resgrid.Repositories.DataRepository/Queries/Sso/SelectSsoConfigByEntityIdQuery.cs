using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.Sso
{
	public class SelectSsoConfigByEntityIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;

		public SelectSsoConfigByEntityIdQuery(SqlConfiguration sqlConfiguration)
			=> _sqlConfiguration = sqlConfiguration;

		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				return $"SELECT * FROM {_sqlConfiguration.SchemaName}.departmentssoconfigs WHERE entityid = {_sqlConfiguration.ParameterNotation}EntityId LIMIT 1";

			return $"SELECT TOP 1 * FROM {_sqlConfiguration.SchemaName}.[DepartmentSsoConfigs] WHERE [EntityId] = {_sqlConfiguration.ParameterNotation}EntityId";
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}

