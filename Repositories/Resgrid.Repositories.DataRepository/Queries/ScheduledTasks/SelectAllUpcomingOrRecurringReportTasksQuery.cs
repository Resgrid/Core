using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.ScheduledTasks
{
	public class SelectAllUpcomingOrRecurringReportTasksQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectAllUpcomingOrRecurringReportTasksQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectAllUpcomingOrRecurringReportTasksQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					string.Empty,
					_sqlConfiguration.ParameterNotation,
					new string[] {
						"%DATETIME%"
					},
					new string[] {
						"DateTime",
					},
					new string[] {
						"%SCHEDULEDTASKSTABLE%",
						"%DEPARTMENTSTABLE%",
						"%ASPNETUSERSTABLE%",
					},
					new string[] {
						_sqlConfiguration.ScheduledTasksTable,
						_sqlConfiguration.DepartmentsTable,
						_sqlConfiguration.UserTable,
					}
				);

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
