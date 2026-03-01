using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.Sso
{
	public class SelectSsoConfigByDepartmentIdAndTypeQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;

		public SelectSsoConfigByDepartmentIdAndTypeQuery(SqlConfiguration sqlConfiguration)
			=> _sqlConfiguration = sqlConfiguration;

		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				return $"SELECT * FROM {_sqlConfiguration.SchemaName}.departmentssoconfigs WHERE departmentid = {_sqlConfiguration.ParameterNotation}DepartmentId AND ssoprovidertype = {_sqlConfiguration.ParameterNotation}SsoProviderType LIMIT 1";

			return $"SELECT TOP 1 * FROM {_sqlConfiguration.SchemaName}.[DepartmentSsoConfigs] WHERE [DepartmentId] = {_sqlConfiguration.ParameterNotation}DepartmentId AND [SsoProviderType] = {_sqlConfiguration.ParameterNotation}SsoProviderType";
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}

