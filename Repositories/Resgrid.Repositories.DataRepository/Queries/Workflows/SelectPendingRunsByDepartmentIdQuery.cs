using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.Workflows
{
	public class SelectPendingRunsByDepartmentIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectPendingRunsByDepartmentIdQuery(SqlConfiguration sqlConfiguration) => _sqlConfiguration = sqlConfiguration;

		// Status 0=Pending, 1=Running
		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				return $"SELECT * FROM {_sqlConfiguration.SchemaName}.workflowruns WHERE departmentid = {_sqlConfiguration.ParameterNotation}DepartmentId AND status IN (0, 1) ORDER BY startedon DESC";

			return $"SELECT * FROM {_sqlConfiguration.SchemaName}.[WorkflowRuns] WHERE [DepartmentId] = {_sqlConfiguration.ParameterNotation}DepartmentId AND [Status] IN (0, 1) ORDER BY [StartedOn] DESC";
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}

