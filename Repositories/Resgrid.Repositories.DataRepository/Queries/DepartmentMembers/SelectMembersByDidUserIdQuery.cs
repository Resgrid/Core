using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.DepartmentMembers
{
	public class SelectMembersByDidUserIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectMembersByDidUserIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectMembersByDidUserIdQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					string.Empty,
					_sqlConfiguration.ParameterNotation,
					new string[] {
						"%DID%",
						"%USERID%" 
					},
					new string[] {
						"DepartmentId",
						"UserId" ,
					},
					new string[] {
						"%DEPARTMENTMEMBERSTABLE%",
						"%ASPNETUSERSTABLE%",
						"%USERPROFILESTABLE%"
					},
					new string[] {
						_sqlConfiguration.DepartmentMembersTable,
						_sqlConfiguration.UserTable,
						_sqlConfiguration.UserProfilesTable
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
