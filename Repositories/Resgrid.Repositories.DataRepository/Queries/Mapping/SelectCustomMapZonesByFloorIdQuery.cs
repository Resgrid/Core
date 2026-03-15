using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Mapping
{
	public class SelectCustomMapZonesByFloorIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;

		public SelectCustomMapZonesByFloorIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectCustomMapZonesByFloorIdQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					string.Empty,
					_sqlConfiguration.ParameterNotation,
					new string[]
					{
						"%CUSTOMMAPFLOORID%"
					},
					new string[]
					{
						"CustomMapFloorId"
					},
					new string[]
					{
						"%CUSTOMMAPZONESTABLE%"
					},
					new string[]
					{
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

