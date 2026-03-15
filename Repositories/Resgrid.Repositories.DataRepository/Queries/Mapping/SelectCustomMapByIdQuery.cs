using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Mapping
{
	public class SelectCustomMapByIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;

		public SelectCustomMapByIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectCustomMapByIdQuery
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
						"%CUSTOMMAPSTABLE%",
						"%CUSTOMMAPFLOORSTABLE%"
					},
					new string[]
					{
						_sqlConfiguration.CustomMapsTableName,
						_sqlConfiguration.CustomMapFloorsTableName
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

