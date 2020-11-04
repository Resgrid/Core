using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.DepartmentSettings
{
	public class SelectAllManagerInfoQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectAllManagerInfoQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectAllDepartmentManagerInfoQuery
				.ReplaceQueryParameters(_sqlConfiguration.SchemaName,
					_sqlConfiguration.DepartmentSettingsTable,
					_sqlConfiguration.ParameterNotation,
					new string[] {  },
					new string[] {  });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
