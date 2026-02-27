using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;

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
			if (DataConfig.DatabaseType == DatabaseTypes.Postgres)
				return $"SELECT * FROM {_sqlConfiguration.SchemaName}.workflows WHERE departmentid = {_sqlConfiguration.ParameterNotation}DepartmentId AND triggereventtype = {_sqlConfiguration.ParameterNotation}TriggerEventType AND isenabled = true";

			return $"SELECT * FROM {_sqlConfiguration.SchemaName}.[Workflows] WHERE [DepartmentId] = {_sqlConfiguration.ParameterNotation}DepartmentId AND [TriggerEventType] = {_sqlConfiguration.ParameterNotation}TriggerEventType AND [IsEnabled] = 1";
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity => GetQuery();
	}
}

