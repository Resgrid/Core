using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Routes
{
	public class SelectRouteStopsByRoutePlanIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectRouteStopsByRoutePlanIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectRouteStopsByRoutePlanIdQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.RouteStopsTableName,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%RPID%" },
					new string[] { "RoutePlanId" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
