using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.CustomMaps
{
	public class SelectCustomMapTileQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectCustomMapTileQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectCustomMapTileQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.CustomMapTilesTableName,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%LAYERID%", "%ZOOM%", "%TX%", "%TY%" },
					new string[] { "CustomMapLayerId", "ZoomLevel", "TileX", "TileY" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
