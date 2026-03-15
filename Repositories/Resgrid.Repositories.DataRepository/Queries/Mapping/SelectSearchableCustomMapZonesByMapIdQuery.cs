using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Mapping
{
	public class SelectSearchableCustomMapZonesByMapIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;

		public SelectSearchableCustomMapZonesByMapIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectSearchableCustomMapZonesByMapIdQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					string.Empty,
					_sqlConfiguration.ParameterNotation,
					new string[]
					{
						"%CUSTOMMAPID%"
					},
					new string[]
					{
						"CustomMapId"
					},
					new string[]
					{
						"%CUSTOMMAPFLOORSTABLE%",
						"%CUSTOMMAPZONESTABLE%"
					},
					new string[]
					{
						_sqlConfiguration.CustomMapFloorsTableName,
						_sqlConfiguration.CustomMapZonesTableName
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

