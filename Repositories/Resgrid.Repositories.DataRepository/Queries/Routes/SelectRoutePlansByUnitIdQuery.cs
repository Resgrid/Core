using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Routes
{
	public class SelectRoutePlansByUnitIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectRoutePlansByUnitIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectRoutePlansByUnitIdQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.RoutePlansTableName,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%UNITID%" },
					new string[] { "UnitId" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
