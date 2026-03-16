using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.IndoorMaps
{
	public class SearchIndoorMapZonesQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SearchIndoorMapZonesQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SearchIndoorMapZonesQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.IndoorMapZonesTableName,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%TERM%", "%DID%" },
					new string[] { "SearchTerm", "DepartmentId" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
