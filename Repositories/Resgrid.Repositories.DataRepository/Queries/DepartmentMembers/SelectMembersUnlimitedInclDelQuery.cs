using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.DepartmentMembers
{
	public class SelectMembersUnlimitedInclDelQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectMembersUnlimitedInclDelQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectMembersUnlimitedInclDelQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					string.Empty,
					_sqlConfiguration.ParameterNotation,
					new string[] {
						"%ID%",
					},
					new string[] {
						"DepartmentId"
					},
					new string[] {
						"%DEPARTMENTMEMBERSTABLE%",
						"%ASPNETUSERSTABLE%"
					},
					new string[] {
						_sqlConfiguration.DepartmentMembersTable,
						_sqlConfiguration.UserTable
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
