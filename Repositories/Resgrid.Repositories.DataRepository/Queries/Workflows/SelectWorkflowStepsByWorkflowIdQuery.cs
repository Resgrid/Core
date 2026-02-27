using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.Workflows
{
	public class SelectWorkflowStepsByWorkflowIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectWorkflowStepsByWorkflowIdQuery(SqlConfiguration sqlConfiguration) => _sqlConfiguration = sqlConfiguration;

		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				return $"SELECT * FROM {_sqlConfiguration.SchemaName}.workflowsteps WHERE workflowid = {_sqlConfiguration.ParameterNotation}WorkflowId ORDER BY steporder ASC";

			return $"SELECT * FROM {_sqlConfiguration.SchemaName}.[WorkflowSteps] WHERE [WorkflowId] = {_sqlConfiguration.ParameterNotation}WorkflowId ORDER BY [StepOrder] ASC";
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}

