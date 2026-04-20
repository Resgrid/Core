using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Calls
{
	/// <summary>
	/// Returns all active <see cref="Call"/> records that have CheckInTimersEnabled = 1
	/// and that a specific user has been dispatched on, scoped to a single department.
	/// Used by Endpoint 1 of the personnel check-in status feature.
	/// </summary>
	public class SelectActiveCallsWithCheckInTimersForUserQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;

		public SelectActiveCallsWithCheckInTimersForUserQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectActiveCallsWithCheckInTimersForUserQuery
				.ReplaceQueryParameters(
					_sqlConfiguration,
					_sqlConfiguration.SchemaName,
					_sqlConfiguration.CallsTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%USERID%", "%DID%" },
					new string[] { "UserId", "DepartmentId" },
					new string[] { "%CALLDISPATCHTABLE%" },
					new string[] { _sqlConfiguration.CallDispatchesTable });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			return GetQuery();
		}
	}
}
