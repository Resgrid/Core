using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.WeatherAlerts
{
	/// <summary>
	/// Counts a department's active, unexpired alerts at or above a severity, one row per severity. Covered by
	/// IX_WeatherAlerts_DepartmentId_Status_Severity (M0312).
	/// </summary>
	public class SelectActiveWeatherAlertSeverityCountsByDepartmentIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectActiveWeatherAlertSeverityCountsByDepartmentIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectActiveWeatherAlertSeverityCountsByDepartmentIdQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.WeatherAlertsTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%DEPARTMENTID%", "%MAXSEVERITY%", "%NOW%" },
					new string[] { "DepartmentId", "MaxSeverity", "Now" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
