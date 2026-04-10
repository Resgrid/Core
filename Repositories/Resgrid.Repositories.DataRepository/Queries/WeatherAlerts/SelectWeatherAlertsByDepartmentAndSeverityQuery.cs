using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.WeatherAlerts
{
	public class SelectWeatherAlertsByDepartmentAndSeverityQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectWeatherAlertsByDepartmentAndSeverityQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectWeatherAlertsByDepartmentAndSeverityQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.WeatherAlertsTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%DEPARTMENTID%", "%MAXSEVERITY%" },
					new string[] { "DepartmentId", "MaxSeverity" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
