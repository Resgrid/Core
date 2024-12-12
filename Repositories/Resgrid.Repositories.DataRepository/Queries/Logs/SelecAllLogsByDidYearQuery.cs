using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Logs
{
	public class SelecAllLogsByDidYearQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelecAllLogsByDidYearQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelecAllLogsByDidYearQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.LogsTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%DID%", "%YEAR%" },
					new string[] { "DepartmentId", "Year" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
