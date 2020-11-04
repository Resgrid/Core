using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.PersonnelRoles
{
	public class SelectRoleUsersByUserQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectRoleUsersByUserQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectRoleUsersByUserQuery
				.ReplaceQueryParameters(_sqlConfiguration.SchemaName,
					_sqlConfiguration.PersonnelRoleUsersTable,
					_sqlConfiguration.ParameterNotation,
					new string[] { "%USERID%", "%DID%" },
					new string[] { "UserId", "DepartmentId" });

			return query;
		}

		public string GetQuery<TEntity>() where TEntity : class, IEntity
		{
			throw new System.NotImplementedException();
		}
	}
}
