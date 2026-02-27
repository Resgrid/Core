using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.Workflows
{
	public class SelectRunsInLastMinuteByDepartmentIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectRunsInLastMinuteByDepartmentIdQuery(SqlConfiguration sqlConfiguration) => _sqlConfiguration = sqlConfiguration;

		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				return $"SELECT * FROM {_sqlConfiguration.SchemaName}.workflowruns WHERE departmentid = {_sqlConfiguration.ParameterNotation}DepartmentId AND queuedon >= {_sqlConfiguration.ParameterNotation}SinceTime";

			return $"SELECT * FROM {_sqlConfiguration.SchemaName}.[WorkflowRuns] WHERE [DepartmentId] = {_sqlConfiguration.ParameterNotation}DepartmentId AND [QueuedOn] >= {_sqlConfiguration.ParameterNotation}SinceTime";
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}

