using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.Workflows
{
	/// <summary>Deletes all run-log rows for every run belonging to a workflow.</summary>
	public class DeleteWorkflowRunLogsByWorkflowIdQuery : IDeleteQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public DeleteWorkflowRunLogsByWorkflowIdQuery(SqlConfiguration sqlConfiguration) => _sqlConfiguration = sqlConfiguration;

		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				return $"DELETE FROM {_sqlConfiguration.SchemaName}.workflowrunlogs WHERE workflowrunid IN (SELECT workflowrunid FROM {_sqlConfiguration.SchemaName}.workflowruns WHERE workflowid = {_sqlConfiguration.ParameterNotation}WorkflowId)";

			return $"DELETE FROM {_sqlConfiguration.SchemaName}.[WorkflowRunLogs] WHERE [WorkflowRunId] IN (SELECT [WorkflowRunId] FROM {_sqlConfiguration.SchemaName}.[WorkflowRuns] WHERE [WorkflowId] = {_sqlConfiguration.ParameterNotation}WorkflowId)";
		}

		public string GetQuery<TEntity>(TEntity entity) where TEntity : class, IEntity => GetQuery();
	}
}

