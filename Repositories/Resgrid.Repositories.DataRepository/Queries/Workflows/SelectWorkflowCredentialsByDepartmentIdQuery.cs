using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.Workflows
{
	public class SelectWorkflowCredentialsByDepartmentIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectWorkflowCredentialsByDepartmentIdQuery(SqlConfiguration sqlConfiguration) => _sqlConfiguration = sqlConfiguration;

		public string GetQuery() =>
			$"SELECT * FROM {_sqlConfiguration.SchemaName}.[WorkflowCredentials] WHERE [DepartmentId] = {_sqlConfiguration.ParameterNotation}DepartmentId";

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}

