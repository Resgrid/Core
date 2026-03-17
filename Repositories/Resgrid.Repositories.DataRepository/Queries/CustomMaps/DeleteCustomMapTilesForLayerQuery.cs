using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.CustomMaps
{
	public class DeleteCustomMapTilesForLayerQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public DeleteCustomMapTilesForLayerQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.DeleteCustomMapTilesForLayerQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.CustomMapTilesTableName,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%LAYERID%" },
					new string[] { "CustomMapLayerId" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
