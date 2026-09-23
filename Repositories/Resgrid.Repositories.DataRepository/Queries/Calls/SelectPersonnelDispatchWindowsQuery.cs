using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Calls
{
	public class SelectPersonnelDispatchWindowsQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectPersonnelDispatchWindowsQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectPersonnelDispatchWindowsQuery
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
						"%CALLDISPATCHESTABLE%",
						"%CALLDISPATCHGROUPSTABLE%",
						"%CALLDISPATCHROLESTABLE%",
						"%DEPARTMENTGROUPMEMBERSTABLE%",
						"%PERSONNELROLEUSERSTABLE%"
					},
					new string[] {
						_sqlConfiguration.CallsTable,
						_sqlConfiguration.CallDispatchesTable,
						_sqlConfiguration.CallDispatchGroupsTable,
						_sqlConfiguration.CallDispatchRolesTable,
						_sqlConfiguration.DepartmentGroupMembersTable,
						_sqlConfiguration.PersonnelRoleUsersTable
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
