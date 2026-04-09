using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.WeatherAlerts
{
	public class SelectActiveWeatherAlertSourcesForPollingQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectActiveWeatherAlertSourcesForPollingQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectActiveWeatherAlertSourcesForPollingQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.WeatherAlertSourcesTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { },
					new string[] { });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
