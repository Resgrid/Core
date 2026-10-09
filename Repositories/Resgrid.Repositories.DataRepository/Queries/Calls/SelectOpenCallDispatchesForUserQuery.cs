using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.Calls
{
	public class SelectOpenCallDispatchesForUserQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectOpenCallDispatchesForUserQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectOpenCallDispatchesForUserQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					string.Empty,
					_sqlConfiguration.ParameterNotation,
					new string[] {
						"%DID%",
						"%USERID%",
						"%NOW%"
					},
					new string[] {
						"DepartmentId",
						"UserId",
						"Now"
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
