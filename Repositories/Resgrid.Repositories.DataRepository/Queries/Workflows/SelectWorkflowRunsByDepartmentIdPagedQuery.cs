using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

namespace Resgrid.Repositories.DataRepository.Queries.Workflows
{
	public class SelectWorkflowRunsByDepartmentIdPagedQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectWorkflowRunsByDepartmentIdPagedQuery(SqlConfiguration sqlConfiguration) => _sqlConfiguration = sqlConfiguration;

		public string GetQuery()
		{
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				return $"SELECT * FROM {_sqlConfiguration.SchemaName}.workflowruns WHERE departmentid = {_sqlConfiguration.ParameterNotation}DepartmentId ORDER BY startedon DESC LIMIT {_sqlConfiguration.ParameterNotation}PageSize OFFSET {_sqlConfiguration.ParameterNotation}Offset";

			return $"SELECT * FROM {_sqlConfiguration.SchemaName}.[WorkflowRuns] WHERE [DepartmentId] = {_sqlConfiguration.ParameterNotation}DepartmentId ORDER BY [StartedOn] DESC OFFSET {_sqlConfiguration.ParameterNotation}Offset ROWS FETCH NEXT {_sqlConfiguration.ParameterNotation}PageSize ROWS ONLY";
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}

