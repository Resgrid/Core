using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Calls
{
	public class SelectUnitDispatchWindowsQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectUnitDispatchWindowsQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectUnitDispatchWindowsQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					string.Empty,
					_sqlConfiguration.ParameterNotation,
					new string[] {
						"%DID%",
						"%STARTDATE%",
						"%ENDDATE%",
						"%LOGGEDFROM%"
					},
					new string[] {
						"DepartmentId",
						"StartDate",
						"EndDate",
						"LoggedFrom"
					},
					new string[] {
						"%CALLSTABLE%",
						"%CALLDISPATCHUNITSTABLE%"
					},
					new string[] {
						_sqlConfiguration.CallsTable,
						_sqlConfiguration.CallDispatchUnitsTable
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
