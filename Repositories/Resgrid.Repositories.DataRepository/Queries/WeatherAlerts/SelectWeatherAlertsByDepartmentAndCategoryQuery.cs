using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.WeatherAlerts
{
	public class SelectWeatherAlertsByDepartmentAndCategoryQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectWeatherAlertsByDepartmentAndCategoryQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectWeatherAlertsByDepartmentAndCategoryQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.WeatherAlertsTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%DEPARTMENTID%", "%CATEGORY%" },
					new string[] { "DepartmentId", "Category" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
