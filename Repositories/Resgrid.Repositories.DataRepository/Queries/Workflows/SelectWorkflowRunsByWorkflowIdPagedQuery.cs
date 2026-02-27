using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.Workflows
{
	public class SelectWorkflowRunsByWorkflowIdPagedQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectWorkflowRunsByWorkflowIdPagedQuery(SqlConfiguration sqlConfiguration) => _sqlConfiguration = sqlConfiguration;

		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				return $"SELECT * FROM {_sqlConfiguration.SchemaName}.workflowruns WHERE workflowid = {_sqlConfiguration.ParameterNotation}WorkflowId ORDER BY startedon DESC LIMIT {_sqlConfiguration.ParameterNotation}PageSize OFFSET {_sqlConfiguration.ParameterNotation}Offset";

			return $"SELECT * FROM {_sqlConfiguration.SchemaName}.[WorkflowRuns] WHERE [WorkflowId] = {_sqlConfiguration.ParameterNotation}WorkflowId ORDER BY [StartedOn] DESC OFFSET {_sqlConfiguration.ParameterNotation}Offset ROWS FETCH NEXT {_sqlConfiguration.ParameterNotation}PageSize ROWS ONLY";
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}

