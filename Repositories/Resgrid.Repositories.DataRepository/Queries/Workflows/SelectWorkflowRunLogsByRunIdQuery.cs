using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.Workflows
{
	public class SelectWorkflowRunLogsByRunIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectWorkflowRunLogsByRunIdQuery(SqlConfiguration sqlConfiguration) => _sqlConfiguration = sqlConfiguration;

		public string GetQuery() =>
			$"SELECT * FROM {_sqlConfiguration.SchemaName}.[WorkflowRunLogs] WHERE [WorkflowRunId] = {_sqlConfiguration.ParameterNotation}WorkflowRunId ORDER BY [StartedOn] ASC";

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}

