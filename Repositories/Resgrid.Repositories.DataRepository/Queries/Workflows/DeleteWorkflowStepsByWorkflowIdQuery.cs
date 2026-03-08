using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.Workflows
{
	public class DeleteWorkflowStepsByWorkflowIdQuery : IDeleteQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public DeleteWorkflowStepsByWorkflowIdQuery(SqlConfiguration sqlConfiguration) => _sqlConfiguration = sqlConfiguration;

		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				return $"DELETE FROM {_sqlConfiguration.SchemaName}.workflowsteps WHERE workflowid = {_sqlConfiguration.ParameterNotation}WorkflowId";

			return $"DELETE FROM {_sqlConfiguration.SchemaName}.[WorkflowSteps] WHERE [WorkflowId] = {_sqlConfiguration.ParameterNotation}WorkflowId";
		}

		public string GetQuery<TEntity>(TEntity entity) where TEntity : class, IEntity => GetQuery();
	}
}

