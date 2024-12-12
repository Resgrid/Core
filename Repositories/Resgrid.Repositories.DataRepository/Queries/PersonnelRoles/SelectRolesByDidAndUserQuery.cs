using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.PersonnelRoles
{
	public class SelectRolesByDidAndUserQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectRolesByDidAndUserQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectRolesByDidAndUserQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					string.Empty,
					_sqlConfiguration.ParameterNotation,
					new string[] {
						"%USERID%",
						"%DID%"
					},
					new string[] {
						"UserId",
						"DepartmentId"
					},
					new string[] {
						"%PERSONNELROLESTABLE%",
						"%PERSONNELROLEUSERSTABLE%"
					},
					new string[] {
						_sqlConfiguration.PersonnelRolesTable,
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
