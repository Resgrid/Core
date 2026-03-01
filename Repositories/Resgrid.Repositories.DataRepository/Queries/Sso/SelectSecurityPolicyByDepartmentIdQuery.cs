using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.Sso
{
	public class SelectSecurityPolicyByDepartmentIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;

		public SelectSecurityPolicyByDepartmentIdQuery(SqlConfiguration sqlConfiguration)
			=> _sqlConfiguration = sqlConfiguration;

		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				return $"SELECT * FROM {_sqlConfiguration.SchemaName}.departmentsecuritypolicies WHERE departmentid = {_sqlConfiguration.ParameterNotation}DepartmentId LIMIT 1";

			return $"SELECT TOP 1 * FROM {_sqlConfiguration.SchemaName}.[DepartmentSecurityPolicies] WHERE [DepartmentId] = {_sqlConfiguration.ParameterNotation}DepartmentId";
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}

