using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Calls
{
	public class SelectOpenCallUnitDispatchesForUnitQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectOpenCallUnitDispatchesForUnitQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectOpenCallUnitDispatchesForUnitQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					string.Empty,
					_sqlConfiguration.ParameterNotation,
					new string[] {
						"%DID%",
						"%UNITID%",
						"%NOW%"
					},
					new string[] {
						"DepartmentId",
						"UnitId",
						"Now"
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
