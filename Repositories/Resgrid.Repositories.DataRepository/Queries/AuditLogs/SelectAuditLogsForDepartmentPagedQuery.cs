using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.AuditLogs
{
	public class SelectAuditLogsForDepartmentPagedQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectAuditLogsForDepartmentPagedQuery(SqlConfiguration sqlConfiguration) => _sqlConfiguration = sqlConfiguration;

		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				return $"SELECT * FROM {_sqlConfiguration.SchemaName}.auditlogs WHERE departmentid = {_sqlConfiguration.ParameterNotation}DepartmentId AND loggedon >= {_sqlConfiguration.ParameterNotation}StartDate AND loggedon < {_sqlConfiguration.ParameterNotation}EndDate ORDER BY loggedon DESC LIMIT {_sqlConfiguration.ParameterNotation}PageSize OFFSET {_sqlConfiguration.ParameterNotation}Offset";

			return $"SELECT * FROM {_sqlConfiguration.SchemaName}.[AuditLogs] WHERE [DepartmentId] = {_sqlConfiguration.ParameterNotation}DepartmentId AND [LoggedOn] >= {_sqlConfiguration.ParameterNotation}StartDate AND [LoggedOn] < {_sqlConfiguration.ParameterNotation}EndDate ORDER BY [LoggedOn] DESC OFFSET {_sqlConfiguration.ParameterNotation}Offset ROWS FETCH NEXT {_sqlConfiguration.ParameterNotation}PageSize ROWS ONLY";
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}
