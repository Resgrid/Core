using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.UserProfiles
{
	public class SelectAllProfilesByDIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectAllProfilesByDIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectAllProfilesByDIdQuery
										 .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
																 string.Empty,
																 _sqlConfiguration.ParameterNotation,
																new string[] {
																				"%DID%"
																			  },
																 new string[] {
																				"DepartmentId",
																			  },
																 new string[] {
																				"%USERPROFILESTABLE%",
																				"%DEPARTMENTMEMBERSTABLE%",
																				"%ASPNETUSERSTABLE%"
																 },
																 new string[] {
																				_sqlConfiguration.UserProfilesTable,
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
