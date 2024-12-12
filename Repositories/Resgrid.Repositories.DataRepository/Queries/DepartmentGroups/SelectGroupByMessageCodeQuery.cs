using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;

namespace Resgrid.Repositories.DataRepository.Queries.DepartmentGroups
{
	public class SelectGroupByMessageCodeQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectGroupByMessageCodeQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectGroupByMessageCodeQuery
										 .ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
																 string.Empty,
																 _sqlConfiguration.ParameterNotation,
																new string[] {
																				"%CODE%"
																			  },
																 new string[] {
																				"MessageEmail",
																			  },
																 new string[] {
																				"%GROUPSTABLE%",
																				"%GROUPMEMBERSSTABLE%"
																 },
																 new string[] {
																				_sqlConfiguration.DepartmentGroupsTable,
																				_sqlConfiguration.DepartmentGroupMembersTable
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
