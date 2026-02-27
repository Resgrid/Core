using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.Workflows
{
	public class SelectWorkflowDailyUsageQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;

		public SelectWorkflowDailyUsageQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				return $"SELECT * FROM {_sqlConfiguration.SchemaName}.workflowdailyusages WHERE departmentid = {_sqlConfiguration.ParameterNotation}DepartmentId AND actiontype = {_sqlConfiguration.ParameterNotation}ActionType AND usagedate = {_sqlConfiguration.ParameterNotation}UsageDate";

			return $"SELECT * FROM {_sqlConfiguration.SchemaName}.[WorkflowDailyUsages] WHERE [DepartmentId] = {_sqlConfiguration.ParameterNotation}DepartmentId AND [ActionType] = {_sqlConfiguration.ParameterNotation}ActionType AND [UsageDate] = {_sqlConfiguration.ParameterNotation}UsageDate";
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}

