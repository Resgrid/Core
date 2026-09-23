using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Calls
{
	public class SelectCallDispatchRolesForCallsInRangeQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectCallDispatchRolesForCallsInRangeQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectCallDispatchRolesForCallsInRangeQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					string.Empty,
					_sqlConfiguration.ParameterNotation,
					new string[] {
						"%DID%",
						"%STARTDATE%",
						"%ENDDATE%"
					},
					new string[] {
						"DepartmentId",
						"StartDate",
						"EndDate"
					},
					new string[] {
						"%CALLSTABLE%",
						"%CALLDISPATCHROLESTABLE%"
					},
					new string[] {
						_sqlConfiguration.CallsTable,
						_sqlConfiguration.CallDispatchRolesTable
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
