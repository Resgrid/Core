using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.Workflows
{
	/// <summary>Deletes all run-log rows for a single workflow run.</summary>
	public class DeleteWorkflowRunLogsByWorkflowRunIdQuery : IDeleteQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public DeleteWorkflowRunLogsByWorkflowRunIdQuery(SqlConfiguration sqlConfiguration) => _sqlConfiguration = sqlConfiguration;

		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				return $"DELETE FROM {_sqlConfiguration.SchemaName}.workflowrunlogs WHERE workflowrunid = {_sqlConfiguration.ParameterNotation}WorkflowRunId";

			return $"DELETE FROM {_sqlConfiguration.SchemaName}.[WorkflowRunLogs] WHERE [WorkflowRunId] = {_sqlConfiguration.ParameterNotation}WorkflowRunId";
		}

		public string GetQuery<TEntity>(TEntity entity) where TEntity : class, IEntity => GetQuery();
	}
}



