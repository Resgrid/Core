using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.Sso
{
	public class SelectSsoConfigsByDepartmentIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;

		public SelectSsoConfigsByDepartmentIdQuery(SqlConfiguration sqlConfiguration)
			=> _sqlConfiguration = sqlConfiguration;

		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				return $"SELECT * FROM {_sqlConfiguration.SchemaName}.departmentssoconfigs WHERE departmentid = {_sqlConfiguration.ParameterNotation}DepartmentId";

			return $"SELECT * FROM {_sqlConfiguration.SchemaName}.[DepartmentSsoConfigs] WHERE [DepartmentId] = {_sqlConfiguration.ParameterNotation}DepartmentId";
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}

