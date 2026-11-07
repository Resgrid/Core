using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Calls
{
	/// <summary>
	/// Counts a department's active calls dispatched to a member: to them, to their group, to a role they hold or to a unit
	/// they have a seat on — the apps' "calls I'm on" rule (isCurrentUserOnCall), in one statement. A call scheduled for
	/// later is left out until it dispatches (as SelectOpenCallDispatchesForUserQuery does): its dispatch rows exist
	/// already, but nobody has been notified.
	/// </summary>
	public class SelectActiveCallsCountDispatchedToUserQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectActiveCallsCountDispatchedToUserQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectActiveCallsCountDispatchedToUserQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					_sqlConfiguration.CallsTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%DID%", "%USERID%", "%NOW%" },
					new string[] { "DepartmentId", "UserId", "Now" },
					new string[] { "%CALLDISPATCHESTABLE%", "%CALLDISPATCHGROUPSTABLE%", "%CALLDISPATCHROLESTABLE%", "%CALLDISPATCHUNITSTABLE%", "%GROUPMEMBERSTABLE%", "%ROLEUSERSTABLE%", "%UNITACTIVEROLESTABLE%" },
					new string[] { _sqlConfiguration.CallDispatchesTable, _sqlConfiguration.CallDispatchGroupsTable, _sqlConfiguration.CallDispatchRolesTable, _sqlConfiguration.CallDispatchUnitsTable, _sqlConfiguration.DepartmentGroupMembersTable, _sqlConfiguration.PersonnelRoleUsersTable, _sqlConfiguration.UnitActiveRolesTable });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
