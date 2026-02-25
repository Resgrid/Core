using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Workflows
{
	public class SelectActiveWorkflowsByDeptAndEventTypeQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;

		public SelectActiveWorkflowsByDeptAndEventTypeQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			return $"SELECT * FROM {_sqlConfiguration.SchemaName}.[Workflows] WHERE [DepartmentId] = {_sqlConfiguration.ParameterNotation}DepartmentId AND [TriggerEventType] = {_sqlConfiguration.ParameterNotation}TriggerEventType AND [IsEnabled] = 1";
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}

