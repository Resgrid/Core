using Resgrid.Model;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Extensions;
using static Resgrid.Framework.Testing.TestData;
using Stripe;
using System.Text.RegularExpressions;

namespace Resgrid.Repositories.DataRepository.Queries.DepartmentGroups
{
	public class SelectGroupMembersByGroupIdQuery : ISelectQuery
	{
		private readonly SqlConfiguration _sqlConfiguration;
		public SelectGroupMembersByGroupIdQuery(SqlConfiguration sqlConfiguration)
		{
			_sqlConfiguration = sqlConfiguration;
		}

		public string GetQuery()
		{
			var query = _sqlConfiguration.SelectGroupMembersByGroupIdQuery
				.ReplaceQueryParameters(_sqlConfiguration, _sqlConfiguration.SchemaName,
					string.Empty,
					_sqlConfiguration.ParameterNotation,
					new string[] {
						"%GROUPID%"
					},
					new string[] {
						"GroupId"
					},
					new string[] {
						"%GROUPSTABLE%",
						"%GROUPMEMBERSSTABLE%",
						"%DEPARTMENTMEMBERSTABLE%"
					},
					new string[] {
						_sqlConfiguration.DepartmentGroupsTable,
						_sqlConfiguration.DepartmentGroupMembersTable,
						_sqlConfiguration.DepartmentMembersTable
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
