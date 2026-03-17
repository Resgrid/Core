using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.CustomMaps
{
	public class SelectPendingCustomMapImportsQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectPendingCustomMapImportsQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectPendingCustomMapImportsQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.CustomMapImportsTableName,
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
