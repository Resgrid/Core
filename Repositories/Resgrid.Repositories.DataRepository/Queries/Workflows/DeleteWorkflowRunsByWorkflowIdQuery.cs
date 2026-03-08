using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.Workflows
{
	public class DeleteWorkflowRunsByWorkflowIdQuery : IDeleteQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public DeleteWorkflowRunsByWorkflowIdQuery(SqlConfiguration sqlConfiguration) => _sqlConfiguration = sqlConfiguration;

		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				return $"DELETE FROM {_sqlConfiguration.SchemaName}.workflowruns WHERE workflowid = {_sqlConfiguration.ParameterNotation}WorkflowId";

			return $"DELETE FROM {_sqlConfiguration.SchemaName}.[WorkflowRuns] WHERE [WorkflowId] = {_sqlConfiguration.ParameterNotation}WorkflowId";
		}

		public string GetQuery<TEntity>(TEntity entity) where TEntity : class, IEntity => GetQuery();
	}
}

